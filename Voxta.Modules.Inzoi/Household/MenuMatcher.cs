using System.Text.RegularExpressions;

namespace Voxta.Modules.Inzoi.Household;

/// <summary>
/// Finds what a character means ("mop the floor", "give her a massage", "change into pajamas") in
/// the game's live menu: everything the Zoi could click right now, on every object at home and
/// toward another Zoi. Each entry is matched by its menu name, the game's hint text, its internal id
/// and the object it belongs to ("Use (Clean 3 Times)" on the mop, id Object_Mopping_Home), so plain
/// words find the right entry without a fixed list.
/// </summary>
public static partial class MenuMatcher
{
    /// <param name="TargetId">The object or Zoi the interaction is done at or with.</param>
    /// <param name="TargetWords">What the target is, in words: "mop", "kitchen range", "Paul".</param>
    /// <param name="Social">An interaction toward another Zoi rather than an object.</param>
    public sealed record Entry(Game.Offer Offer, long TargetId, string TargetWords, bool Social);

    public sealed record Match(Entry Entry, double Score);

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "to", "some", "my", "your", "his", "her", "their", "our", "with", "and", "or", "for", "of",
        "go", "going", "do", "doing", "does", "have", "having", "get", "let", "lets", "let's", "i", "ill", "i'll", "im",
        "i'm", "will", "now", "okay", "ok", "fine", "then", "it", "this", "that", "on", "in", "at", "up", "a bit",
        "little", "bit", "quick", "quickly", "together", "me", "you", "him", "them", "us", "we", "please", "just",
        "use", "start", "make", "take",
    };

    /// <summary>Everyday words for what the game calls something else.</summary>
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mop"] = ["mopping"], ["sweep"] = ["mopping", "mop"], ["vacuum"] = ["vaccum", "vc"], ["hoover"] = ["vaccum", "vc"],
        ["floor"] = ["mopping", "mop", "vaccum"], ["tidy"] = ["tidy", "clean"], ["mess"] = ["mopping", "mop", "vaccum"], ["dirty"] = ["clean"],
        ["dishes"] = ["dish"], ["laundry"] = ["laundry", "washingmachine"], ["clothes"] = ["laundry", "outfit"],
        ["trash"] = ["trash", "empty"], ["garbage"] = ["trash"], ["bin"] = ["trash"], ["rubbish"] = ["trash"],
        ["plants"] = ["water", "pot"], ["flowers"] = ["water", "pot"], ["plant"] = ["water", "pot"],
        ["fix"] = ["repair"], ["broken"] = ["repair"],
        ["nap"] = ["doze", "sleep"], ["bed"] = ["sleep"], ["rest"] = ["relax", "sit", "doze"],
        ["couch"] = ["sofa"], ["tv"] = ["watch", "tv"], ["telly"] = ["watch", "tv"], ["show"] = ["watch"], ["series"] = ["drama", "watch"],
        ["food"] = ["eat", "cook"], ["meal"] = ["cook", "eat"], ["dinner"] = ["cook", "eat"], ["lunch"] = ["cook", "eat"], ["breakfast"] = ["cook", "eat"], ["snack"] = ["eat"],
        ["workout"] = ["exercise", "pushup", "squat"], ["gym"] = ["exercise"], ["train"] = ["exercise"], ["pushups"] = ["pushup", "push-ups"],
        ["music"] = ["audio", "listen"], ["song"] = ["sing", "serenade"], ["sing"] = ["serenade", "sing"],
        ["cuddle"] = ["hug", "lap", "arm", "shoulder"], ["smooch"] = ["kiss"], ["snog"] = ["kiss"], ["peck"] = ["peck", "kiss"],
        ["chat"] = ["talk"], ["argue"] = ["debate", "nitpick", "accuse", "argue"], ["fight"] = ["fight", "debate", "nitpick"], ["insult"] = ["insult", "accuse", "nitpick"],
        ["joke"] = ["joke"], ["photo"] = ["photo", "selfie", "pic"], ["picture"] = ["photo", "pic"],
        ["pajamas"] = ["sleepwear"], ["pyjamas"] = ["sleepwear"], ["dress"] = ["outfit", "clothes"], ["change"] = ["change", "outfit"],
        ["game"] = ["game", "play"], ["games"] = ["game", "play"], ["read"] = ["book", "read"],
        ["teeth"] = ["teeth", "brush"], ["face"] = ["face", "wash"], ["hands"] = ["hand", "wash"],
        ["toilet"] = ["toilet"], ["pee"] = ["toilet"], ["bathroom"] = ["toilet", "bath", "shower"],
        ["coffee"] = ["coffee"], ["drink"] = ["drink", "water"], ["thirsty"] = ["drink"], ["hungry"] = ["eat"],
        ["comfort"] = ["cheer", "hug", "comfort"], ["console"] = ["cheer", "hug", "comfort"], ["apologize"] = ["apolog", "sorry"], ["apologise"] = ["apolog", "sorry"],
        ["date"] = ["date"], ["massage"] = ["massage"], ["propose"] = ["propose"], ["marry"] = ["propose"],
    };

    /// <summary>
    /// The best entries for what the character said, best first. Entries the game greys out are
    /// matched too (the caller can say "nothing needs that right now"), but rank below usable ones.
    /// </summary>
    public static IReadOnlyList<Match> Find(string intent, IEnumerable<Entry> entries, bool preferSocial, int max = 3, double minScore = 0.9)
    {
        var words = Words(intent).Where(w => !Stopwords.Contains(w)).Distinct().ToArray();
        if (words.Length == 0) return [];

        var results = new List<Match>();
        foreach (var entry in entries)
        {
            var display = Words(entry.Offer.DisplayName).ToArray();
            var detail = Words($"{entry.Offer.Hint} {SplitId(entry.Offer.BagId)} {entry.TargetWords}").ToArray();

            double score = 0;
            var matched = 0;
            foreach (var word in words)
            {
                var best = Math.Max(Similarity(word, display) * 1.5, Similarity(word, detail));
                if (Synonyms.TryGetValue(word, out var alternatives))
                {
                    foreach (var alt in alternatives)
                        best = Math.Max(best, Math.Max(Similarity(alt, display), Similarity(alt, detail)) * 0.7);
                }

                if (best > 0) matched++;
                score += best;
            }

            if (matched == 0) continue;
            score /= words.Length;
            // All the meaningful words found is worth more than one word found strongly.
            score *= 0.6 + 0.4 * matched / words.Length;
            if (entry.Social == preferSocial) score *= 1.15;
            if (!entry.Offer.Enabled) score *= 0.8;
            // A child's bed or toy is the wrong pick for "go to bed", unless a child was meant.
            if (entry.TargetWords.Contains("kid", StringComparison.OrdinalIgnoreCase) && !words.Any(w => w is "kid" or "child" or "kids" or "children"))
                score *= 0.85;
            // Shorter menu names are the plain version ("Kiss" over "Kiss at the Table").
            score *= 1 + 0.15 / Math.Max(1, display.Length);
            results.Add(new Match(entry, score));
        }

        return results
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(max)
            .ToArray();
    }

    /// <summary>1 for the same word, 0.75 for the same stem ("mopping" and "mop", "dishes" and "dish"), else 0.</summary>
    private static double Similarity(string word, IReadOnlyList<string> candidates)
    {
        double best = 0;
        foreach (var c in candidates)
        {
            if (c.Equals(word, StringComparison.OrdinalIgnoreCase)) return 1;
            var stem = Stem(word);
            var other = Stem(c);
            if (stem.Length >= 3 && other.Length >= 3 && (stem == other || (stem.Length >= 4 && other.StartsWith(stem, StringComparison.Ordinal)) || (other.Length >= 4 && stem.StartsWith(other, StringComparison.Ordinal))))
                best = 0.75;
        }

        return best;
    }

    private static string Stem(string word)
    {
        var w = word.ToLowerInvariant();
        foreach (var suffix in new[] { "ping", "ming", "ning", "ing", "ies", "es", "ed", "s" })
        {
            if (w.Length - suffix.Length >= 3 && w.EndsWith(suffix, StringComparison.Ordinal))
                return suffix == "ies" ? w[..^3] + "y" : w[..^suffix.Length];
        }

        return w;
    }

    public static IEnumerable<string> Words(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : WordPattern().Matches(text.Replace("'", "")).Select(m => m.Value.ToLowerInvariant());

    /// <summary>"Object_Mopping_Home" → "Object Mopping Home"; "Co_SitSofa_LapPillow" → "Co Sit Sofa Lap Pillow".</summary>
    public static string SplitId(string id) =>
        CamelBoundary().Replace(id.Replace('_', ' '), " ");

    /// <summary>"KitchenRange01_V04" → "kitchen range".</summary>
    public static string ObjectWords(string tableId)
    {
        var name = tableId.StartsWith("D_", StringComparison.Ordinal) || tableId.StartsWith("U_", StringComparison.Ordinal) ? tableId[2..] : tableId;
        name = name.Split('_')[0];
        return CamelBoundary().Replace(TrailingDigits().Replace(name, ""), " ").ToLowerInvariant();
    }

    [GeneratedRegex(@"[\p{L}][\p{L}\-]*")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])")]
    private static partial Regex CamelBoundary();

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingDigits();
}
