using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Voxta.Model.Shared;
using Voxta.Modules.Inzoi.Game;
using Voxta.Modules.Inzoi.Household;

namespace Voxta.Modules.Inzoi.ChatAugmentations;

/// <summary>
/// Lets a character make their Zoi do whatever they do in the chat, as long as the game offers it.
/// Voxta's action inference says what, in the character's own words ("mop the floor", "give Paul a
/// massage"); this reads the game's live menu (every object at home and the other Zoi), narrows it
/// down by words, lets the language model pick the one entry that means it, and queues it, so the Zoi
/// really walks over and does it.
/// </summary>
public sealed partial class GameActions(InzoiGame game, ILogger logger)
{
    public const string DoAction = "inzoi_do";

    /// <summary>Asks the chat's language model a short question; null when it can't.</summary>
    public delegate Task<string?> Generate(string system, string user, CancellationToken cancellationToken);

    /// <summary>Above this many candidates the model would only be guessing from a wall of text.</summary>
    private const int MaxCandidates = 40;

    private readonly InteractionMenu _menu = new(game);

    /// <summary>
    /// The action definitions for this household. <paramref name="expressionChoices"/> null leaves out
    /// the emotions and gestures.
    /// </summary>
    public static ScenarioActionDefinition[] Definitions(IEnumerable<string> zoiNames, bool gameActions, string[]? expressionChoices)
    {
        var names = zoiNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var actions = new List<ScenarioActionDefinition>();

        if (expressionChoices is { Length: > 0 })
        {
            actions.Add(new ScenarioActionDefinition
            {
                Name = Expressions.ExpressAction,
                // Its own layer: showing a feeling doesn't use up the chance to also do something.
                Layer = Expressions.Layer,
                Description = "Your Zoi shows a clear emotion or gesture with their whole body in the game (inZOI): laughing, crying, clapping, being angry, a pose... Use it when your reply has a strong, visible reaction. Not for calm, ordinary talk.",
                ShortDescription = "Show an emotion or gesture",
                Timing = FunctionTiming.AfterAssistantMessage,
                Arguments =
                [
                    new FunctionArgumentDefinition
                    {
                        Name = "expression",
                        Type = FunctionArgumentType.String,
                        Description = "The emotion or gesture that fits the reply best",
                        Required = true,
                        Choices = expressionChoices,
                    },
                ],
            });
        }

        if (!gameActions) return actions.ToArray();

        actions.Add(new ScenarioActionDefinition
        {
            Name = DoAction,
            Description = "Your Zoi does something in the game (inZOI): anything a person does at home or with a housemate, like cooking, eating, sleeping, showering, mopping the floor, doing the laundry, watching TV, playing chess, working out, changing clothes, or kissing, hugging, comforting, arguing with or giving a massage to someone. Use it when your reply does it, starts it, or agrees to do it right now (\"okay, I'll mop the floor\"). Not for things only mentioned, planned for later, or asked of someone else.",
            ShortDescription = "Do something in the game",
            Timing = FunctionTiming.AfterAssistantMessage,
            Arguments =
            [
                new FunctionArgumentDefinition
                {
                    Name = "what",
                    Type = FunctionArgumentType.String,
                    Description = "What your Zoi does, in a few plain English words, e.g. \"mop the floor\", \"take a shower\", \"kiss\", \"give a massage\", \"change into pajamas\", \"cook dinner\"",
                    Required = true,
                },
                new FunctionArgumentDefinition
                {
                    Name = "with",
                    Type = FunctionArgumentType.String,
                    Description = "The household member it is done with or to, by first name, or \"nobody\"",
                    Required = false,
                    Choices = [.. names, "nobody"],
                },
            ],
        });

        return actions.ToArray();
    }

    /// <summary>Runs an action for the Zoi. Returns a line for the character's memory of what happened.</summary>
    public async Task<string> RunAsync(
        ZoiInfo actor,
        GameSnapshot state,
        IReadOnlyDictionary<string, object?> arguments,
        ZoiMatcher matcher,
        Generate generate,
        CancellationToken cancellationToken)
    {
        string? Arg(string name) => arguments.TryGetValue(name, out var v) ? v?.ToString()?.Trim() : null;

        var what = Arg("what");
        if (string.IsNullOrWhiteSpace(what)) return $"{actor.FirstName} wanted to do something, but it wasn't clear what.";

        var withName = Arg("with");
        ZoiInfo? with = null;
        if (!string.IsNullOrWhiteSpace(withName) && !withName.Equals("nobody", StringComparison.OrdinalIgnoreCase))
        {
            with = matcher.Match(withName, state.Zois);
            if (with is not null && with.Id == actor.Id) with = null;
        }

        var entries = await _menu.BuildAsync(actor, with, cancellationToken);
        if (entries.Count == 0)
            return $"{actor.FirstName} wanted to {what}, but the game offers nothing to do right now (only at home, while the game runs).";

        var intent = with is null ? what : $"{what} with {with.FirstName}";
        var pick = await PickAsync(actor, intent, entries, with is not null, generate, cancellationToken);
        if (pick is null)
        {
            logger.LogInformation("{Actor}: nothing in the game's menu matches '{Intent}'", actor.FullName, intent);
            return $"{actor.FirstName} wanted to {what}, but that isn't something the game offers right now.";
        }

        if (!pick.Offer.Enabled)
            return $"{actor.FirstName} wanted to {what}, but in the game there's no need right now ({pick.Offer.DisplayName} isn't available; probably already done).";

        var problem = await game.ExecuteInteractionAsync(actor.Id, pick.Offer.BagId, pick.TargetId, cancellationToken);
        logger.LogInformation("{Actor}: '{Intent}' → {Interaction} at {Target} ({Bag}) {Result}", actor.FullName, intent, pick.Offer.DisplayName, pick.TargetWords, pick.Offer.BagId, problem ?? "queued");
        _menu.Forget();

        return problem is null
            ? $"In the game, {actor.FirstName} goes to: {pick.Offer.DisplayName} ({(pick.Social ? "with " : "at the ")}{pick.TargetWords})."
            : $"{actor.FirstName} tried to {what} in the game, but it didn't work ({problem}).";
    }

    /// <summary>
    /// Narrows the menu to the likely entries by words, then lets the language model choose. Without a
    /// model (or when it can't decide), a clear word match is taken on its own.
    /// </summary>
    private async Task<MenuMatcher.Entry?> PickAsync(ZoiInfo actor, string intent, List<MenuMatcher.Entry> entries, bool social, Generate generate, CancellationToken cancellationToken)
    {
        // The same menu name at two identical objects is one choice.
        var distinct = entries
            .GroupBy(e => (e.Offer.DisplayName, e.TargetWords, e.Social))
            .Select(g => g.OrderByDescending(e => e.Offer.Enabled).First())
            .ToList();

        var ranked = MenuMatcher.Find(intent, distinct, social, MaxCandidates, minScore: 0.3);
        // Words in another language, or phrased far from the game's wording: let the model see the menu itself.
        var candidates = ranked.Count > 0 && ranked[0].Score >= 1.0
            ? ranked.Select(r => r.Entry).ToList()
            : Shortlist(distinct, social, ranked);

        if (candidates.Count == 0) return null;

        var answer = await AskAsync(actor, intent, candidates, generate, cancellationToken);
        if (answer is { } index)
            return index == 0 ? null : candidates[index - 1];

        // No model answer: only a clear word match is safe to act on.
        return ranked.Count > 0 && ranked[0].Score >= 1.3 ? ranked[0].Entry : null;
    }

    /// <summary>The word matches first, then the rest of the usable menu, up to what the model can read.</summary>
    private static List<MenuMatcher.Entry> Shortlist(List<MenuMatcher.Entry> entries, bool social, IReadOnlyList<MenuMatcher.Match> ranked)
    {
        var result = ranked.Select(r => r.Entry).ToList();
        foreach (var entry in entries
                     .Where(e => e.Offer.Enabled)
                     .OrderByDescending(e => e.Social == social))
        {
            if (result.Count >= MaxCandidates * 5) break;
            if (!result.Contains(entry)) result.Add(entry);
        }

        return result;
    }

    private async Task<int?> AskAsync(ZoiInfo actor, string intent, List<MenuMatcher.Entry> candidates, Generate generate, CancellationToken cancellationToken)
    {
        var list = new StringBuilder();
        for (var i = 0; i < candidates.Count; i++)
        {
            var e = candidates[i];
            list.Append(i + 1).Append(". ").Append(e.Offer.DisplayName)
                .Append(e.Social ? " — with " : " — at the ").Append(e.TargetWords);
            if (!string.IsNullOrWhiteSpace(e.Offer.Hint) && !e.Offer.Hint.Equals(e.Offer.DisplayName, StringComparison.OrdinalIgnoreCase))
                list.Append(" (").Append(e.Offer.Hint).Append(')');
            if (!e.Offer.Enabled) list.Append(" [not needed right now]");
            list.AppendLine();
        }

        const string system = "You match what a life-simulation character is doing to one entry of the game's interaction menu. Pick the entry that does what was meant, at the most fitting object or with the right person. If nothing on the menu really does it, answer 0. Answer with the number only.";
        var user = $"{actor.FirstName} does: \"{intent}\"\n\nMenu:\n{list}\nNumber:";

        string? reply;
        try
        {
            reply = await generate(system, user, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exc)
        {
            logger.LogDebug(exc, "The language model couldn't pick a menu entry");
            return null;
        }

        if (reply is null) return null;
        var number = FirstNumber().Match(reply);
        if (!number.Success || !int.TryParse(number.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0 || n > candidates.Count)
        {
            logger.LogDebug("Unclear menu pick from the language model: '{Reply}'", reply);
            return null;
        }

        logger.LogDebug("Menu pick for '{Intent}': {Pick} of {Count}", intent, n == 0 ? "none" : candidates[n - 1].Offer.DisplayName, candidates.Count);
        return n;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex FirstNumber();
}
