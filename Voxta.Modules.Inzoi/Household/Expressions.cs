using System.Globalization;

namespace Voxta.Modules.Inzoi.Household;

/// <summary>
/// The emotions and gestures a character can show with its Zoi's whole body: laughing, crying,
/// clapping, a cat-heart pose... Each choice lists inZOI's own animations for it, best first. Not
/// every animation suits every Zoi (age, gender, sitting or standing), so the first one the game
/// accepts for that Zoi is played. The ids come from inZOI's CharacterAnim table (inZOI 0.11).
/// </summary>
public static class Expressions
{
    public const string ExpressAction = "inzoi_express";

    /// <summary>A separate action layer, so a character can kiss someone and laugh in the same reply.</summary>
    public const string Layer = "inzoi_expression";

    /// <param name="Choice">What the character picks.</param>
    /// <param name="Face">The facial expression (facial.set) that goes with it.</param>
    /// <param name="Animations">Standing animations, best first.</param>
    /// <param name="Seated">Animations for a seated Zoi, tried first when they seem to be sitting.</param>
    public sealed record Expression(string Choice, string? Face, string[] Animations, string[]? Seated = null);

    public static readonly Expression[] Builtin =
    [
        new("laugh", "Laughing", ["EmoReact_Funny_Laughing", "Self_Event_Laugh", "Self_Active_React_Funny"]),
        new("laugh at someone", "Laughing", ["Self_Event_LaughAt", "Reaction_LaughingAt"]),
        new("giggle", "Giggling", ["Self_Emotion_Giggling", "EmoReact_Funny_Giggling"]),
        new("be amused", "Funny", ["EmoReact_Funny_Reaction_Funny_Lv2", "EmoReact_Funny_Funny_Short"]),
        new("be happy", "Joyful", ["Self_Emotion_Joyful", "Self_Emotion_Happy", "EmoReact_Calm_Joyful"]),
        new("be delighted", "Delighted", ["EmoReact_Calm_Delighted", "Reaction_PleasantSurprise_F", "Reaction_PleasantSurprise"]),
        new("be excited", "Thrilled", ["EmoReact_Excited_Enthusiastic", "EmoReact_Calm_Thrilled", "EmoReact_Excited_Reaction_Excited_Lv2", "Self_Excited_Short"]),
        new("clap", "Joyful", ["Self_Emotion_Clapping"], Seated: ["Self_Emotion_Clapping_Sit"]),
        new("cheer someone on", "Excited", ["Self_Emotion_CheerUp", "Self_Emotion_CheerUp_2"]),
        new("be proud", "Confident", ["Self_Emotion_Confident", "EmoReact_Convinced_Confident"]),
        new("be smitten", "Sensual", ["Self_Emotion_Dazzled", "EmoReact_Enchanting_Dazzled", "EmoReact_Enchanting_Flutter"]),
        new("be touched", "Deeplymoved", ["Self_Emotion_Deeplymoved", "EmoReact_Touched_Deeplymoved", "EmoReact_Touched_Reaction_Touched_Lv2"]),
        new("have an idea", "Eureka", ["EmoReact_Curious_Eureka", "EmoReact_Curious_Ingenious"]),
        new("think it over", "Preoccupied", ["Self_Emotion_Thinking", "Self_Thinking"]),
        new("be sad", "Sad", ["Self_Emotion_Sad", "Self_Active_React_Sad", "EmoReact_Sad_Reaction_Sad_Lv2"]),
        new("be teary", "Depressed", ["EmoReact_Sad_Reaction_Tearful_Lv2"]),
        new("cry", "Heartbroken", ["EmoReact_Sad_Cry_Stand", "EmoReact_Sad_Cry_F", "EmoReact_Sad_Cry_M"]),
        new("be heartbroken", "Heartbroken", ["EmoReact_Sad_Heartbroken"]),
        new("be annoyed", "Irritated", ["EmoReact_Irritated_Irritated_Short", "Self_Angry_NoHand"]),
        new("be angry", "Angry", ["EmoReact_Irritated_GetAngry_Stand", "Self_Angry", "Self_Active_React_Furious"]),
        new("be furious", "Furious", ["EmoReact_Irritated_Furious", "EmoReact_Irritated_GetFurious", "EmoReact_Irritated_Reaction_Furious_Lv2"]),
        new("be stressed", "Irritated", ["EmoReact_Irritated_Reaction_Stressed_Lv2"]),
        new("be disgusted", "Disgusted", ["EmoReact_Uncomfortable_Disgusted"]),
        new("refuse", "Uncomfortable", ["Self_Active_React_Refuse"]),
        new("be nervous", "Nervous", ["EmoReact_Uneasy_Reaction_Uneasy_Lv2"]),
        new("be scared", "Afraid", ["Self_Active_React_Scared"]),
        new("be terrified", "Terrified", ["EmoReact_Worried_Terrified"]),
        new("be surprised", "Surprised", ["Self_Surprised", "Reaction_Surprised_Lv2_F", "Reaction_Surprised_Lv2", "EmoReact_Surprised_Reaction_Surprised_Lv2"]),
        new("be shocked", "Shocked", ["EmoReact_Surprised_Shocked"]),
        new("be embarrassed", "Embarassed", ["EmoReact_Cringing_Embarassed"]),
        new("be humiliated", "Humiliating", ["EmoReact_Cringing_Humiliating"]),
        new("be tired", "Tired", ["EmoReact_Tired_Tired", "EmoReact_Tired_Reaction_Tired_Lv2"]),
        new("yawn", "Tired", ["Stand_Yawn"]),
        new("stretch", "Calm", ["Stand_Stretching"]),
        new("sigh", "Uncomfortable", ["Stand_Sigh"]),
        new("be bored", "Bored", ["Self_Bored_Short", "Event_Stand_Bored"]),
        new("dance", "Joyful", ["Self_Dance", "Dance01", "Dance02", "Dance03", "Dance04", "Dance05"]),
        new("do a silly dance", "Giggling", ["SillyDance"]),
        new("enjoy the music", "Joyful", ["Self_Emotion_Enjoy_Music"]),
        new("make a cat-heart pose", "Joyful", ["Self_Pose_CatHeart"]),
        new("strike a selfie pose", "Joyful", ["Selfie_Pose"]),
        new("show off muscles", "Confident", ["Muscle_Brag_Self", "Mirror_MusclePose"]),
        new("pray", "Calm", ["Silent_Prayer"]),
        new("playfully scare someone", "Giggling", ["Self_Emotion_Boo_Around"]),
    ];

    /// <summary>A user's own animation: a game or mod animation id, or one of their AI emotes.</summary>
    public sealed record Custom(string Choice, string? AnimationId, int? AiSlot);

    public const string AiMotionPrefix = "Self_GeneratedMotionSlot";

    /// <summary>
    /// Lines like <c>deep kiss = Lover_DeepKiss_MF</c> or <c>my dance = ai:3</c> (AI emote slot 3,
    /// counting from 1 like the game's list). Blank lines and lines starting with # are skipped.
    /// </summary>
    public static Custom[] ParseCustom(string? lines)
    {
        var result = new List<Custom>();
        if (string.IsNullOrWhiteSpace(lines)) return [];
        foreach (var raw in lines.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var choice = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (choice.Length == 0 || value.Length == 0) continue;

            if (value.StartsWith("ai:", StringComparison.OrdinalIgnoreCase))
            {
                // The game has 30 AI emote slots.
                if (int.TryParse(value[3..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot) && slot is >= 1 and <= 30)
                    result.Add(new Custom(choice, null, slot - 1));
            }
            else if (!value.Contains(' '))
                result.Add(new Custom(choice, value, null));
        }

        return result.DistinctBy(c => c.Choice, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Everything a character can choose: the user's own first, so they win over a built-in of the same name.</summary>
    public static string[] Choices(IEnumerable<Custom> custom) =>
        custom.Select(c => c.Choice)
            .Concat(Builtin.Select(b => b.Choice))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static Expression? FindBuiltin(string? choice) =>
        Builtin.FirstOrDefault(b => b.Choice.Equals(choice?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The order to try a built-in's animations for this Zoi: seated ones first when sitting, the Zoi's gender's _F/_M variant before the other.</summary>
    public static IEnumerable<string> Candidates(Expression expression, bool seated, string? gender)
    {
        var all = seated && expression.Seated is { } sit ? sit.Concat(expression.Animations) : expression.Animations.Concat(expression.Seated ?? []);
        var mine = gender?.StartsWith("f", StringComparison.OrdinalIgnoreCase) == true ? "_F"
            : gender?.StartsWith("m", StringComparison.OrdinalIgnoreCase) == true ? "_M" : null;
        var other = mine == "_F" ? "_M" : mine == "_M" ? "_F" : null;
        // Stable: keeps the listed order, but the other gender's variant goes last.
        return all.OrderBy(a => other is not null && a.EndsWith(other, StringComparison.Ordinal) ? 1 : 0);
    }

    /// <summary>A Zoi in the middle of one of these shouldn't be interrupted by a gesture: it would break what they're doing.</summary>
    public static bool IsBusy(Game.ActivityInfo? activity)
    {
        if (activity is null) return false;
        var id = activity.InteractionId;
        return BusyWords.Any(w => id.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] BusyWords = ["Sleep", "Bath", "Shower", "Toilet", "Drive", "Swim", "Cook", "Craft", "Mopping", "Vacuum", "VC_", "Laundry", "WashingMachine", "WashDishes", "Clean", "Repair"];
}
