using System.Text.RegularExpressions;

namespace Voxta.Modules.Inzoi.Household;

/// <summary>
/// Cuts a reply into speech bubbles. A bubble above a Zoi's head holds a sentence or two, not a
/// paragraph, so a long reply becomes a few bubbles shown one after another, each long enough to read.
/// </summary>
public static partial class Bubbles
{
    public const int MaxBubbleLength = 160;

    /// <summary>Hard cap on how much of one reply goes into the game; the chat has the rest.</summary>
    public const int MaxBubbles = 6;

    public sealed record Bubble(string Text, double Seconds);

    public static IReadOnlyList<Bubble> Split(string? reply, bool hideActions)
    {
        if (string.IsNullOrWhiteSpace(reply)) return [];

        var text = reply;
        if (hideActions) text = Actions().Replace(text, " ");
        // Markdown and emotion tags that read badly in the game.
        text = Tags().Replace(text, " ");
        text = text.Replace("**", "").Replace("__", "");
        text = Whitespace().Replace(text, " ").Trim();
        if (text.Length == 0) return [];

        var sentences = SentenceEnd().Split(text).Select(s => s.Trim()).Where(s => s.Length > 0);
        var bubbles = new List<string>();
        var current = "";
        foreach (var sentence in sentences)
        {
            foreach (var piece in Wrap(sentence))
            {
                if (current.Length == 0) current = piece;
                else if (current.Length + 1 + piece.Length <= MaxBubbleLength) current += " " + piece;
                else
                {
                    bubbles.Add(current);
                    current = piece;
                }
            }
        }

        if (current.Length > 0) bubbles.Add(current);

        if (bubbles.Count > MaxBubbles)
        {
            bubbles = bubbles.Take(MaxBubbles).ToList();
            bubbles[^1] = bubbles[^1].TrimEnd('.', '!', '?', ',') + "…";
        }

        return bubbles.Select(b => new Bubble(b, ReadingSeconds(b))).ToArray();
    }

    /// <summary>About as long as it takes to read it, within reason.</summary>
    public static double ReadingSeconds(string text) => Math.Clamp(2.5 + text.Length * 0.065, 4, 14);

    /// <summary>A sentence longer than a bubble is cut at words.</summary>
    private static IEnumerable<string> Wrap(string sentence)
    {
        if (sentence.Length <= MaxBubbleLength)
        {
            yield return sentence;
            yield break;
        }

        var line = "";
        foreach (var word in sentence.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > MaxBubbleLength)
            {
                yield return line;
                line = "";
            }

            line = line.Length == 0 ? word : line + " " + word;
        }

        if (line.Length > 0) yield return line;
    }

    [GeneratedRegex(@"\*[^*\n]+\*")]
    private static partial Regex Actions();

    // [laughs] style tags some TTS setups use, and leftover HTML.
    [GeneratedRegex(@"\[[^\]\n]{1,40}\]|<[^>\n]{1,40}>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceEnd();
}
