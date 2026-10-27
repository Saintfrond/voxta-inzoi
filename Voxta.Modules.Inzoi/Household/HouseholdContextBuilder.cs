using System.Text;
using Voxta.Modules.Inzoi.Game;

namespace Voxta.Modules.Inzoi.Household;

/// <summary>
/// Turns a household snapshot into the context the characters read. Written to be read by a model,
/// not a person: words over numbers ("very hungry" rather than "hunger 18"), the Zois in this chat in
/// full, everyone else in a line, and nothing that changes every few seconds unless it matters.
/// </summary>
public static class HouseholdContextBuilder
{
    public sealed record Options(string UserRole, string? CustomUserRole);

    /// <param name="participants">Zois played by characters in this chat, keyed by Zoi id, with the character's name.</param>
    public static string Build(GameSnapshot? state, bool connected, IReadOnlyDictionary<long, string> participants, Options options, IReadOnlyList<Happening>? recent = null)
    {
        var text = new StringBuilder();

        if (state is null || !state.InGame || state.Zois.Length == 0)
        {
            text.AppendLine(state is null || !connected
                ? "[inZOI] The game is not running, so there is no news from the household. Stay in character as a member of your inZOI household; don't invent specific current events."
                : "[inZOI] The game is open but not in play right now (menus or loading). Stay in character as a member of your inZOI household; don't invent specific current events.");
            AppendUserRole(text, null, options);
            return text.ToString().TrimEnd();
        }

        var world = state.World;
        var when = Join(", ", world?.DayOfWeek, world?.Time, world?.Season, WeatherPhrase(world));
        text.Append("[inZOI — ");
        text.Append(connected ? "live from the game" : "last known, the game has closed");
        if (!string.IsNullOrWhiteSpace(world?.City)) text.Append(", in ").Append(world!.City);
        if (when.Length > 0) text.Append(", ").Append(when);
        if (world?.Paused == true) text.Append(", game paused");
        text.AppendLine("]");

        if (!string.IsNullOrWhiteSpace(state.HouseholdName))
            text.Append("Household: the ").Append(state.HouseholdName).AppendLine(" family");
        if (!string.IsNullOrWhiteSpace(world?.CalendarEvent))
            text.Append("Today: ").AppendLine(Phrases.Words(world!.CalendarEvent!));

        var inChat = state.Zois.Where(z => participants.ContainsKey(z.Id)).ToArray();
        var others = state.Zois.Where(z => !participants.ContainsKey(z.Id)).ToArray();

        foreach (var zoi in inChat)
            AppendZoiInDetail(text, zoi, participants[zoi.Id], state);

        if (others.Length > 0)
        {
            text.AppendLine("Also in the household:");
            foreach (var zoi in others)
                text.Append("- ").AppendLine(SummaryLine(zoi, state));
        }

        if (recent is { Count: > 0 })
        {
            text.AppendLine("Recently in the game (oldest first; this is what just happened between them, so it explains how they feel now):");
            foreach (var happening in recent)
            {
                text.Append("- ");
                if (!string.IsNullOrWhiteSpace(happening.GameTime)) text.Append(happening.GameTime).Append(' ');
                text.AppendLine(happening.Text);
            }
        }

        AppendUserRole(text, state, options);

        text.AppendLine("Stay true to your Zoi: let their emotion, needs, what they are doing and what just happened between them shape how you talk, the way a person would mention being tired or busy. Don't recite these notes or quote numbers.");
        return text.ToString().TrimEnd();
    }

    private static void AppendZoiInDetail(StringBuilder text, ZoiInfo zoi, string characterName, GameSnapshot state)
    {
        text.Append("* ").Append(zoi.FullName);
        if (!string.Equals(ZoiMatcher.Normalize(characterName), ZoiMatcher.Normalize(zoi.FullName), StringComparison.Ordinal))
            text.Append(" (played by ").Append(characterName).Append(')');
        text.Append(" — ").Append(Describe(zoi));
        if (zoi.IsPossessed) text.Append(", the Zoi the player is controlling right now");
        text.AppendLine();

        var emotion = Phrases.Emotion(zoi.Emotion);
        if (emotion is not null)
        {
            text.Append("  Feeling: ").Append(emotion);
            if (zoi.Emotion is { } e && !e.Type.Equals("Normal", StringComparison.OrdinalIgnoreCase) && !e.Type.Equals("Sleep", StringComparison.OrdinalIgnoreCase))
            {
                var negative = string.Equals(e.Tendency, "negative", StringComparison.OrdinalIgnoreCase);
                if (e.Level >= 3) text.Append(" — overwhelming, it dominates everything they say");
                else if (e.Level >= 2 || negative) text.Append(negative ? " — clearly, and it should show in how they talk" : " — clearly");
            }

            text.AppendLine();
        }

        if (zoi.Activity is { } activity)
            text.Append("  Doing: ").AppendLine(ActivityWithWhom(zoi, activity, state));

        if (!string.IsNullOrWhiteSpace(zoi.Site))
            text.Append("  At: ").AppendLine(zoi.Site);

        var needs = Phrases.Needs(zoi.Needs);
        if (needs is not null) text.Append("  Needs: ").AppendLine(needs);

        text.Append("  Work: ").AppendLine(Phrases.Work(zoi));

        if (zoi.Hopes.Length > 0)
            text.Append("  Wishes right now: ").AppendLine(string.Join("; ", zoi.Hopes.Select(Phrases.Hope)));

        // Acquaintances the Zoi has nothing to say about (a neighbour they nodded to once) are left out.
        var relationships = zoi.Relationships
            .Where(r => r.Id == zoi.SpouseId || r.IsFamily || Math.Abs(r.Friendship) >= 30 || r.Romance >= 15 || r.Romance <= -30)
            .OrderByDescending(r => r.Id == zoi.SpouseId)
            .ThenByDescending(r => r.IsFamily)
            .ThenByDescending(r => Math.Max(r.Friendship, r.Romance))
            .Take(6)
            .Select(r => Phrases.Relationship(r, zoi))
            .ToArray();
        if (relationships.Length > 0)
            text.Append("  Relationships: ").AppendLine(string.Join("; ", relationships));
    }

    /// <summary>"nitpick (with Paul Lez)" when the activity is aimed at someone they know.</summary>
    private static string ActivityWithWhom(ZoiInfo zoi, ActivityInfo activity, GameSnapshot state)
    {
        var what = Phrases.Activity(activity);
        if (activity.TargetId is not { } target || target == zoi.Id) return what;
        var who = state.Zois.FirstOrDefault(z => z.Id == target)?.FullName
                  ?? zoi.Relationships.FirstOrDefault(r => r.Id == target)?.Name;
        return who is null ? what : $"{what} (with {who})";
    }

    private static string SummaryLine(ZoiInfo zoi, GameSnapshot state)
    {
        var parts = new List<string> { $"{zoi.FullName} ({Describe(zoi)})" };
        var emotion = Phrases.Emotion(zoi.Emotion);
        if (emotion is not null) parts.Add(emotion);
        if (zoi.Activity is { } activity) parts.Add($"doing: {ActivityWithWhom(zoi, activity, state)}");
        if (zoi.IsPossessed) parts.Add("the Zoi the player controls");
        return string.Join(", ", parts);
    }

    private static void AppendUserRole(StringBuilder text, GameSnapshot? state, Options options)
    {
        switch (options.UserRole)
        {
            case Choices.UserIsPossessedZoi:
                var possessed = state?.Possessed;
                text.AppendLine(possessed is not null
                    ? $"{{{{ user }}}} is {possessed.FullName}, a member of this household."
                    : "{{ user }} is a member of this household.");
                break;
            case Choices.UserIsCustom when !string.IsNullOrWhiteSpace(options.CustomUserRole):
                text.AppendLine(options.CustomUserRole!.Trim());
                break;
            default:
                text.AppendLine("{{ user }} is the player: the unseen presence who guides this household's lives in the city. The Zois can talk to them directly.");
                break;
        }
    }

    private static string Describe(ZoiInfo zoi)
    {
        var age = string.IsNullOrWhiteSpace(zoi.AgeGroup) ? null : zoi.AgeGroup.ToLowerInvariant();
        var gender = zoi.Gender?.ToLowerInvariant() switch
        {
            "female" => "woman",
            "male" => "man",
            null or "" => null,
            var g => g,
        };
        // A child or teen isn't "a child woman": the age says enough.
        if (age is not null && (age.Contains("child") || age.Contains("teen") || age.Contains("toddler") || age.Contains("infant") || age.Contains("baby")))
            gender = zoi.Gender?.ToLowerInvariant() switch { "female" => "girl", "male" => "boy", _ => null };
        return Join(" ", age, gender) is { Length: > 0 } s ? s : "Zoi";
    }

    private static string? WeatherPhrase(WorldInfo? world)
    {
        if (world is null) return null;
        var weather = string.IsNullOrWhiteSpace(world.Weather) ? null : Phrases.Words(world.Weather!).ToLowerInvariant();
        var temperature = world.Temperature is { } t ? $"{t}°C" : null;
        return Join(" ", weather, temperature) is { Length: > 0 } s ? s : null;
    }

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
