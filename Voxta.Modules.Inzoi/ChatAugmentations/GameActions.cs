using Microsoft.Extensions.Logging;
using Voxta.Model.Shared;
using Voxta.Modules.Inzoi.Game;
using Voxta.Modules.Inzoi.Household;

namespace Voxta.Modules.Inzoi.ChatAugmentations;

/// <summary>
/// The actions a character can take in the game through Voxta's action inference: do something
/// with another Zoi (kiss, hug, dance...) or do an activity (cook, sleep, watch TV...). Voxta decides
/// from the conversation when a character does one; this finds the matching inZOI interaction and
/// queues it, so the Zoi really walks over and does it.
/// </summary>
public sealed class GameActions(InzoiGame game, ILogger logger)
{
    /// <summary>The action definitions for this household. Targets are the other Zois' first names.</summary>
    public static ScenarioActionDefinition[] Definitions(IEnumerable<string> zoiNames)
    {
        var names = zoiNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var actions = new List<ScenarioActionDefinition>();

        if (names.Length > 0)
        {
            actions.Add(new ScenarioActionDefinition
            {
                Name = ActionCatalog.InteractAction,
                Description = "Your Zoi does something with another member of the household in the game (inZOI), like kissing, hugging or dancing with them. Use it when you actually do that in your reply, not when you only talk about it.",
                ShortDescription = "Do something with another Zoi",
                Timing = FunctionTiming.AfterAssistantMessage,
                Arguments =
                [
                    new FunctionArgumentDefinition
                    {
                        Name = "target",
                        Type = FunctionArgumentType.String,
                        Description = "Who, by first name",
                        Required = true,
                        Choices = names,
                    },
                    new FunctionArgumentDefinition
                    {
                        Name = "interaction",
                        Type = FunctionArgumentType.String,
                        Description = "What to do with them",
                        Required = true,
                        Choices = ActionCatalog.SocialChoices.Select(s => s.Choice).ToArray(),
                    },
                ],
            });
        }

        actions.Add(new ScenarioActionDefinition
        {
            Name = ActionCatalog.ActivityAction,
            Description = "Your Zoi starts an everyday activity at home in the game (inZOI), like cooking, eating, sleeping or watching TV. Use it when you actually go and do that in your reply, not when you only talk about it.",
            ShortDescription = "Start an activity at home",
            Timing = FunctionTiming.AfterAssistantMessage,
            Arguments =
            [
                new FunctionArgumentDefinition
                {
                    Name = "activity",
                    Type = FunctionArgumentType.String,
                    Description = "What to do",
                    Required = true,
                    Choices = ActionCatalog.ActivityChoices.Select(a => a.Choice).ToArray(),
                },
            ],
        });

        return actions.ToArray();
    }

    /// <summary>Runs an action for the Zoi. Returns a line for the character's memory of what happened.</summary>
    public async Task<string> RunAsync(ZoiInfo actor, GameSnapshot state, string action, IReadOnlyDictionary<string, object?> arguments, ZoiMatcher matcher, CancellationToken cancellationToken)
    {
        string? Arg(string name) => arguments.TryGetValue(name, out var v) ? v?.ToString() : null;

        if (action == ActionCatalog.InteractAction)
        {
            var choice = ActionCatalog.FindSocial(Arg("interaction"));
            var target = Arg("target") is { } who ? matcher.Match(who, state.Zois) : null;
            if (choice is null) return $"{actor.FirstName} wanted to do something with someone, but it isn't something the game offers.";
            if (target is null || target.Id == actor.Id) return $"{actor.FirstName} wanted to {choice.Choice}, but couldn't find {Arg("target") ?? "them"} in the household.";
            return await SocialAsync(actor, target, choice, cancellationToken);
        }

        if (action == ActionCatalog.ActivityAction)
        {
            var activity = ActionCatalog.FindActivity(Arg("activity"));
            if (activity is null) return $"{actor.FirstName} wanted to do something the game doesn't offer at home.";
            return await ActivityAsync(actor, activity, cancellationToken);
        }

        return $"Unknown action {action}.";
    }

    private async Task<string> SocialAsync(ZoiInfo actor, ZoiInfo target, ActionCatalog.Social choice, CancellationToken cancellationToken)
    {
        // Face to face first: the list the game shows when one Zoi clicks the other.
        var offer = ActionCatalog.Best(await game.ListInteractionsAsync(actor.Id, target.Id, cancellationToken), choice.Names);
        long? at = target.Id;

        // Some things happen at an object together (a massage on the sofa, games at the TV).
        if (offer is null && ActionCatalog.SocialAtObjects.TryGetValue(choice.Choice, out var objectKinds))
        {
            foreach (var obj in ActionCatalog.Candidates(await game.GetObjectsAsync(cancellationToken), o => o.TableId, objectKinds).Take(4))
            {
                offer = ActionCatalog.Best(await game.ListInteractionsAsync(actor.Id, obj.Id, cancellationToken), choice.Names);
                if (offer is null) continue;
                at = obj.Id;
                break;
            }
        }

        if (offer is null)
            return $"{actor.FirstName} wanted to {choice.Choice} with {target.FirstName}, but that isn't possible in the game right now (too far apart, or not that close yet).";

        var problem = await game.ExecuteInteractionAsync(actor.Id, offer.BagId, at, cancellationToken);
        logger.LogInformation("{Actor} → {Target}: {Interaction} ({Bag}) {Result}", actor.FullName, target.FullName, offer.DisplayName, offer.BagId, problem ?? "queued");
        return problem is null
            ? $"In the game, {actor.FirstName} goes to {target.FirstName} to: {offer.DisplayName}."
            : $"{actor.FirstName} tried to {choice.Choice} with {target.FirstName} in the game, but it didn't work ({problem}).";
    }

    private async Task<string> ActivityAsync(ZoiInfo actor, ActionCatalog.Activity activity, CancellationToken cancellationToken)
    {
        var objects = await game.GetObjectsAsync(cancellationToken);
        if (objects.Count == 0)
            return $"{actor.FirstName} wanted to {activity.Choice}, but the game can only find things to use when the Zois are at home.";

        foreach (var obj in ActionCatalog.Candidates(objects, o => o.TableId, activity.Objects, activity.AvoidObjects).Take(5))
        {
            var offer = ActionCatalog.Best(await game.ListInteractionsAsync(actor.Id, obj.Id, cancellationToken), activity.Names, activity.AvoidNames);
            if (offer is null) continue;

            var problem = await game.ExecuteInteractionAsync(actor.Id, offer.BagId, obj.Id, cancellationToken);
            logger.LogInformation("{Actor}: {Interaction} at {Object} ({Bag}) {Result}", actor.FullName, offer.DisplayName, obj.TableId, offer.BagId, problem ?? "queued");
            if (problem is null)
                return $"In the game, {actor.FirstName} goes to: {offer.DisplayName} ({Phrases.InteractionWords(obj.TableId)}).";
        }

        return $"{actor.FirstName} wanted to {activity.Choice}, but there's nothing at home to do that with right now.";
    }
}
