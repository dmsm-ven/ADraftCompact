using ADraftCompact.Models;

namespace ADraftCompact.Services;

public sealed record HeroNameMatch(HeroName? Hero, double Score, double Margin, HeroName? RunnerUp, bool IsNoHero);

/// <summary>
/// Maps noisy OCR text ("SNAPFIR ESE", "MEDUSIQY") to a hero. Pure logic, no OCR or OpenCV dependency.
/// </summary>
public static class HeroNameMatcher
{
    private const string NoHeroText = "NOHERO";

    /// <summary>Letters only, upper case: OCR is unreliable about spaces, hyphens and apostrophes.</summary>
    public static string Normalize(string text)
    {
        Span<char> buffer = stackalloc char[text.Length];
        var length = 0;
        foreach (var ch in text)
            if (char.IsLetter(ch) && ch < 128) buffer[length++] = char.ToUpperInvariant(ch);
        return new string(buffer[..length]);
    }

    /// <summary>Similarity 0..1 (1 = identical after normalization).</summary>
    public static double Similarity(string read, string name)
    {
        var a = Normalize(read);
        var b = Normalize(name);
        if (a.Length == 0 || b.Length == 0) return 0;

        var full = 1.0 - Levenshtein(a, b) / (double)Math.Max(a.Length, b.Length);
        if (a.Length <= b.Length) return full;

        // The art behind the text can add junk after the name: also compare with a prefix of the same length.
        var prefix = 1.0 - Levenshtein(a[..b.Length], b) / (double)b.Length;
        return Math.Max(full, prefix * 0.95);
    }

    /// <summary>
    /// Picks the best hero over all OCR lines. "NO HERO" (the card of a player who has not picked yet)
    /// is recognized by the same text similarity.
    /// </summary>
    public static HeroNameMatch Resolve(IEnumerable<string> lines, IReadOnlyList<HeroName> heroes, double minScore)
    {
        HeroName? best = null, second = null;
        double bestScore = 0, secondScore = 0, noHeroScore = 0;

        foreach (var line in lines)
        {
            noHeroScore = Math.Max(noHeroScore, Similarity(line, NoHeroText));
            foreach (var hero in heroes)
            {
                var score = Similarity(line, hero.Name);
                if (score > bestScore)
                {
                    if (best is not null && best.Value.Id != hero.Id) { second = best; secondScore = bestScore; }
                    best = hero; bestScore = score;
                }
                else if (score > secondScore && best is not null && best.Value.Id != hero.Id)
                {
                    second = hero; secondScore = score;
                }
            }
        }

        if (noHeroScore >= minScore && noHeroScore > bestScore)
            return new HeroNameMatch(null, noHeroScore, noHeroScore - bestScore, null, IsNoHero: true);
        return new HeroNameMatch(best, bestScore, bestScore - secondScore, second, IsNoHero: false);
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
