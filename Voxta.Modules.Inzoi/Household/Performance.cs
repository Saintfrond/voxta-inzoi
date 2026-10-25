using System.Text.RegularExpressions;
using Voxta.Modules.Inzoi.Game;

namespace Voxta.Modules.Inzoi.Household;

/// <summary>
/// How a Zoi looks while their character speaks: which of inZOI's own conversation animations to
/// play, and which facial expression fits the reply. inZOI has no lip sync to drive, so this is
/// what makes a Zoi look like the one talking.
/// </summary>
public static partial class Performance
{
    /// <summary>
    /// inZOI's own conversation animations, the ones Zois use when they talk to each other:
    /// TalkPool_{Positive|Negative|Normal}_{Stand|Sit}_Speaker, plus _02 and _03 variants.
    /// </summary>
    public static IReadOnlyList<string> TalkAnimations(string tone, bool sitting)
    {
        var posture = sitting ? "Sit" : "Stand";
        var basis = $"TalkPool_{tone}_{posture}_Speaker";
        return [basis, basis + "_02", basis + "_03"];
    }

    /// <summary>Positive, Negative or Normal: from the reply's mood if it has a clear one, else the Zoi's emotion.</summary>
    public static string Tone(string? replyFace, EmotionInfo? emotion)
    {
        if (replyFace is not null)
        {
            if (PositiveFaces.Contains(replyFace)) return "Positive";
            if (NegativeFaces.Contains(replyFace)) return "Negative";
        }

        return emotion?.Tendency?.ToLowerInvariant() switch
        {
            "positive" => "Positive",
            "negative" => "Negative",
            _ => "Normal",
        };
    }

    private static readonly HashSet<string> PositiveFaces = new(StringComparer.OrdinalIgnoreCase)
        { "Laughing", "Giggling", "Funny", "Excited", "Thrilled", "Joyful", "Delighted", "Sensual", "Enchanting", "Touched", "Calm", "Convinced", "Curious" };

    private static readonly HashSet<string> NegativeFaces = new(StringComparer.OrdinalIgnoreCase)
        { "Sad", "Depressed", "Heartbroken", "Angry", "Irritated", "Furious", "Afraid", "Worried", "Terrified", "Embarassed", "Cringing", "Disgusted", "Uncomfortable", "Nervous", "Uneasy" };

    /// <summary>
    /// Whether the Zoi is probably sitting, from what they are doing. inZOI doesn't report posture,
    /// and a standing animation on a seated Zoi pulls them out of the chair.
    /// </summary>
    public static bool LooksSeated(ActivityInfo? activity)
    {
        if (activity is null) return false;
        var id = activity.InteractionId;
        return SeatedWords.Any(w => id.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] SeatedWords =
        ["Sofa", "Chair", "Sit", "Bench", "Couch", "Bed", "Toilet", "Desk", "Computer", "Stool", "Armchair", "Eat", "Table", "Bath", "Piano", "Drive", "Car_"];

    /// <summary>
    /// A facial expression for a reply, from what it says: laughter, tears, anger, surprise,
    /// affection. Null when nothing stands out, so the Zoi keeps the face their emotion gives them.
    /// German and English, since that's what the characters will mostly speak.
    /// </summary>
    public static string? FaceFor(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        var text = reply.ToLowerInvariant();
        if (Laugh().IsMatch(text)) return "Laughing";
        if (Love().IsMatch(text)) return "Sensual";
        if (Angry().IsMatch(text)) return "Angry";
        if (Sad().IsMatch(text)) return "Sad";
        if (Scared().IsMatch(text)) return "Afraid";
        if (Surprised().IsMatch(text)) return "Surprised";
        if (Embarrassed().IsMatch(text)) return "Embarassed";
        if (Smile().IsMatch(text)) return "Joyful";
        return null;
    }

    /// <summary>About how long the reply takes to say aloud, for when there's no audio to time it by.</summary>
    public static TimeSpan SpeakingTime(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return TimeSpan.Zero;
        var words = reply.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return TimeSpan.FromSeconds(Math.Clamp(words / 2.6, 2, 60));
    }

    [GeneratedRegex(@"\b(haha|hehe|hihi|lol|lach|laugh|giggl|kicher|grins)|😂|🤣|😆|😄")]
    private static partial Regex Laugh();

    [GeneratedRegex(@"\b(liebe dich|lieb dich|hab dich lieb|i love you|love you|kuss|küss|kiss|schatz|darling|sweetheart|blush|errötet)|😘|😍|🥰|❤|💕")]
    private static partial Regex Love();

    [GeneratedRegex(@"\b(wütend|sauer|verdammt|reicht mir|angry|furious|damn it|how dare|wie kannst du es wagen|knurr|growl)|😠|😡|🤬")]
    private static partial Regex Angry();

    [GeneratedRegex(@"\b(traurig|weint|weinen|schluchz|tränen|vermisse|sad|cry|crying|tears|sob|miss you)|😢|😭|😔|☹")]
    private static partial Regex Sad();

    [GeneratedRegex(@"\b(angst|fürchte|erschrock|gruselig|afraid|scared|terrified|frightened)|😨|😱|😰")]
    private static partial Regex Scared();

    [GeneratedRegex(@"\b(was\?!|wirklich\?!|echt\?!|wow|oh mein gott|omg|no way|what\?!|really\?!|überrascht|surprised)|😮|😲|😯")]
    private static partial Regex Surprised();

    [GeneratedRegex(@"\b(peinlich|verlegen|embarrass|awkward|ähm|äh,|um,)|😳|😅")]
    private static partial Regex Embarrassed();

    [GeneratedRegex(@"\b(lächelt|strahlt|smiles|beams|grinst|freu mich|so glad|wie schön|wonderful|großartig)|😊|🙂|😁")]
    private static partial Regex Smile();
}
