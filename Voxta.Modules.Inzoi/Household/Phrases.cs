using System.Text.RegularExpressions;
using Voxta.Modules.Inzoi.Game;

namespace Voxta.Modules.Inzoi.Household;

/// <summary>
/// Turns the game's ids and numbers into words a model reads well: "very hungry" rather than
/// "Hunger 18", "working out (dumbbell)" rather than "Object_Dumbbell01".
/// </summary>
public static partial class Phrases
{
    public static string? Emotion(EmotionInfo? emotion)
    {
        if (emotion is null) return null;
        if (emotion.Type.Equals("Normal", StringComparison.OrdinalIgnoreCase)) return "fine";
        if (emotion.Type.Equals("Sleep", StringComparison.OrdinalIgnoreCase)) return "asleep";
        var word = emotion.Label ?? Words(emotion.Type);
        return word.ToLowerInvariant();
    }

    /// <summary>Only the needs worth mentioning. A Zoi whose needs are all fine gets "all fine".</summary>
    public static string? Needs(IReadOnlyDictionary<string, double> needs)
    {
        if (needs.Count == 0) return null;
        var notable = needs
            .OrderBy(n => n.Value)
            .Select(n => Need(n.Key, n.Value))
            .Where(p => p is not null)
            .ToArray();
        return notable.Length == 0 ? "all fine" : string.Join(", ", notable);
    }

    /// <summary>How urgent a need is: 0 fine, 1 a bit, 2 very, 3 desperately.</summary>
    public static int NeedLevel(double percent) => percent switch
    {
        < 10 => 3,
        < 25 => 2,
        < 40 => 1,
        _ => 0,
    };

    public static string? Need(string need, double percent)
    {
        var level = NeedLevel(percent);
        if (level == 0) return null;
        var how = level switch { 3 => "desperately", 2 => "very", _ => "a bit" };
        return need.ToLowerInvariant() switch
        {
            "hunger" => $"{how} hungry",
            "sleep" => $"{how} sleepy",
            "energy" => $"{how} tired",
            "toilet" or "bladder" => level == 3 ? "desperately needs the bathroom" : $"{how} needs the bathroom",
            "cleanliness" or "hygiene" => $"{how} in need of a shower",
            "society" or "social" => $"{how} lonely",
            "fun" => $"{how} bored",
            "admit" or "recognition" => $"{how} in need of appreciation",
            "weathering" => $"{how} uncomfortable with the weather",
            // Karma and homesickness aren't moment-to-moment feelings a Zoi would talk about.
            "karma" or "homesick" => null,
            _ => $"{Words(need).ToLowerInvariant()} low",
        };
    }

    /// <summary>The short thing a need asks for, for event lines: "food", "sleep", "the bathroom".</summary>
    public static string NeedObject(string need) => need.ToLowerInvariant() switch
    {
        "hunger" => "food",
        "sleep" => "sleep",
        "energy" => "rest",
        "toilet" or "bladder" => "the bathroom",
        "cleanliness" or "hygiene" => "a shower",
        "society" or "social" => "company",
        "fun" => "some fun",
        "admit" or "recognition" => "appreciation",
        _ => Words(need).ToLowerInvariant(),
    };

    /// <summary>Needs that make for events. Karma, homesickness and weather are left to the context.</summary>
    public static bool IsEventNeed(string need) => need.ToLowerInvariant() is "hunger" or "sleep" or "energy" or "toilet" or "cleanliness" or "society" or "fun";

    private static readonly HashSet<string> GenericActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "PlayLoopAnim", "PlayAnim", "Interaction", "Default", "None", "Use", "UseObject", "Wait", "Idle",
    };

    private static readonly string[] IdPrefixes = ["Object_", "On_", "Co_", "Talk_", "Interaction_", "Social_", "Self_"];

    /// <summary>"Exercise" + "Object_Dumbbell01" → "exercise (dumbbell)"; "On_Audio01_RnB" → "audio RnB".</summary>
    public static string Activity(ActivityInfo activity)
    {
        var what = InteractionWords(activity.InteractionId);
        var action = activity.ActionType;
        if (string.IsNullOrWhiteSpace(action) || GenericActions.Contains(action))
            return what;
        var actionWords = Words(action).ToLowerInvariant();
        if (what.Length == 0 || what.Equals(actionWords, StringComparison.OrdinalIgnoreCase)) return actionWords;
        return $"{actionWords} ({what})";
    }

    public static string InteractionWords(string id)
    {
        var name = id;
        foreach (var prefix in IdPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => TrailingDigits().Replace(p, ""))
            .Where(p => p.Length > 0)
            .Select(Words);
        var text = string.Join(" ", parts);
        // Lower-case ordinary words, keep things like "RnB" or "TV" as they are.
        return string.Join(" ", text.Split(' ').Select(w => w.Skip(1).Any(char.IsUpper) ? w : w.ToLowerInvariant()));
    }

    private static readonly string[] SocialPrefixes = ["Coop_", "Co_", "TalkPool_", "Talk_", "Special_", "Social_"];

    private static readonly HashSet<string> SocialNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "MF", "FM", "MM", "FF", "M", "F", "Main", "Sub", "Host", "Target", "Feedback", "Speaker", "Listener",
        "Stand", "Sit", "Self", "Success", "Lv", "Short", "Long", "Common", "Normal", "Business", "Romance",
        "Friendship", "Family", "Spouse", "Lover", "Friend", "Perspective", "Preference", "Skill", "Talk", "Pool",
        "MaleToFemale", "FemaleToMale", "MaleToMale", "FemaleToFemale", "Male", "Female",
    };

    /// <summary>
    /// Readable words for a social interaction the game has no menu name for: "Talk_Karma_Push" →
    /// "push". Null when only codes are left ("Co_Friendship_G10_2", ordinary small talk), so it isn't
    /// reported as if it meant something.
    /// </summary>
    public static string? SocialWords(string interactionId)
    {
        var name = interactionId;
        foreach (var prefix in SocialPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        var words = name.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.Any(char.IsDigit) && !SocialNoise.Contains(w) && !w.Equals("Karma", StringComparison.OrdinalIgnoreCase))
            .Select(w => Words(w).ToLowerInvariant())
            .ToArray();
        return words.Length == 0 ? null : string.Join(" ", words);
    }

    /// <summary>"Trait_Romantic_ExpressAffection" → "express affection".</summary>
    public static string Hope(string hopeId)
    {
        var last = hopeId.Split('_', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? hopeId;
        return Words(TrailingDigits().Replace(last, "")).ToLowerInvariant();
    }

    /// <summary>"ExpressAffection" → "Express Affection". Runs like "RnB" or "TV" stay together.</summary>
    public static string Words(string identifier) =>
        CamelBoundary().Replace(identifier.Replace('_', ' '), " ").Trim();

    public static string Relationship(RelationshipInfo r, ZoiInfo owner)
    {
        var parts = new List<string>();
        if (owner.SpouseId == r.Id) parts.Add("spouse");
        else if (r.IsFamily) parts.Add("family");

        var friendship = r.Friendship switch
        {
            >= 75 => "close friends",
            >= 30 => "friends",
            > -30 => null,
            _ => "dislike each other",
        };
        if (friendship is not null) parts.Add(friendship);

        if (r.Romance >= 15) parts.Add(r.Romance >= 60 ? "deeply in love" : "romantic");
        else if (r.Romance <= -30) parts.Add("romantic tension gone sour");

        return parts.Count == 0 ? r.Name : $"{r.Name} ({string.Join(", ", parts)})";
    }

    public static string Work(ZoiInfo zoi)
    {
        if (string.IsNullOrWhiteSpace(zoi.JobName)) return "no job";
        var job = string.IsNullOrWhiteSpace(zoi.JobRank) ? zoi.JobName! : $"{zoi.JobRank} ({zoi.JobName})";
        if (zoi.Work is { } w)
        {
            if (w.IsAtWork) return $"{job}, at work right now";
            if (w.IsCommuteTime) return $"{job}, on the way to or from work";
            if (w.IsWorkingTime) return $"{job}, should be at work right now";
        }

        return job;
    }

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingDigits();

    // Between a lower-case letter and an upper-case one that starts a word: "ExpressAffection" splits, "RnB" doesn't.
    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z][a-z])")]
    private static partial Regex CamelBoundary();
}
