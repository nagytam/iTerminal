using System.Text.RegularExpressions;

namespace iTerminal.Security.Headers;

/// <summary>
/// The result of grading one response: a letter from A to F for the five core headers, and the lines to show under the
/// headers the tool printed. <paramref name="Sound"/> is how many of the five are present and sound.
/// </summary>
public sealed record SecurityHeaderGrade(string Letter, int Sound, IReadOnlyList<string> Lines);

/// <summary>
/// Grades the response headers of one HTTP answer against the recommendations of the OWASP HTTP Headers Cheat Sheet. The
/// five core headers are Strict-Transport-Security, Content-Security-Policy, X-Content-Type-Options, frame protection
/// (X-Frame-Options or the frame-ancestors directive) and Referrer-Policy. The cross-origin policies and Permissions-Policy
/// are reported as not sent, without counting. Server and X-Powered-By are flagged for removal, and the deprecated
/// Expect-CT and Public-Key-Pins as deprecated.
/// </summary>
/// <remarks>
/// A grade is a hint about one response, never a verdict on a site: a different path or a different request can send other
/// headers. The HSTS line states the cost too: a long max-age that the certificate cannot back locks users out.
/// The function is pure: it reads the header pairs it is given and touches nothing else.
/// </remarks>
public static class SecurityHeaderGrader
{
    /// <summary>One year in seconds: the lower bound the cheat sheet's HSTS example uses for a sound max-age.</summary>
    internal const long OneYear = 31536000;

    private static readonly Regex MaxAgePattern = new(@"max-age\s*=\s*""?(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly string[] WeakReferrerPolicies = ["unsafe-url", "no-referrer-when-downgrade", "origin", "origin-when-cross-origin"];

    private static readonly string[] NotSentPolicies = ["Cross-Origin-Opener-Policy", "Cross-Origin-Embedder-Policy", "Cross-Origin-Resource-Policy", "Permissions-Policy"];

    private static readonly string[] RemoveHeaders = ["Server", "X-Powered-By"];

    private static readonly string[] DeprecatedHeaders = ["Expect-CT", "Public-Key-Pins"];

    /// <summary>Grades the headers. Names are matched without case; the first value of a repeated name is the one graded.</summary>
    public static SecurityHeaderGrade Grade(IEnumerable<KeyValuePair<string, string>> headers)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> header in headers)
        {
            values.TryAdd(header.Key.Trim(), header.Value.Trim());
        }

        var lines = new List<string>();
        int sound = 0;

        sound += Core(lines, values, "Strict-Transport-Security", GradeHsts);
        sound += Core(lines, values, "Content-Security-Policy", GradeCsp);
        sound += Core(lines, values, "X-Content-Type-Options", GradeContentTypeOptions);
        sound += Core(lines, values, "X-Frame-Options / frame-ancestors", GradeFrameProtection);
        sound += Core(lines, values, "Referrer-Policy", GradeReferrerPolicy);

        foreach (string name in NotSentPolicies.Where(name => !values.ContainsKey(name)))
        {
            lines.Add($"{name}: not sent (recommended, not counted in the grade)");
        }

        foreach (string name in RemoveHeaders.Where(values.ContainsKey))
        {
            lines.Add($"{name}: {values[name]} (remove it: it tells an attacker which software runs here)");
        }

        foreach (string name in DeprecatedHeaders.Where(values.ContainsKey))
        {
            lines.Add($"{name}: deprecated; browsers no longer act on it");
        }

        string letter = sound switch
        {
            5 => "A",
            4 => "B",
            3 => "C",
            2 => "D",
            _ => "F",
        };
        lines.Insert(0, $"Grade: {letter} ({sound} of 5 core headers present and sound; a hint about this response only)");
        return new SecurityHeaderGrade(letter, sound, lines);
    }

    private static int Core(List<string> lines, Dictionary<string, string> values, string label, Func<Dictionary<string, string>, (bool IsSound, string Verdict)> grade)
    {
        (bool isSound, string verdict) = grade(values);
        lines.Add($"{label}: {verdict}");
        return isSound ? 1 : 0;
    }

    private static (bool IsSound, string Verdict) GradeHsts(Dictionary<string, string> values)
    {
        if (!values.TryGetValue("Strict-Transport-Security", out string? value))
        {
            return (false, "missing (only meaningful over HTTPS)");
        }

        Match match = MaxAgePattern.Match(value);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out long maxAge))
        {
            return (false, "weak: no max-age");
        }

        if (maxAge < OneYear)
        {
            return (false, $"weak: max-age is {maxAge} seconds, below one year");
        }

        return (true, "sound (a long max-age locks users out if the certificate lapses, so set it only when HTTPS works everywhere)");
    }

    private static (bool IsSound, string Verdict) GradeCsp(Dictionary<string, string> values)
    {
        if (!values.TryGetValue("Content-Security-Policy", out string? value))
        {
            return (false, "missing");
        }

        if (value.Contains("unsafe-inline", StringComparison.OrdinalIgnoreCase) || value.Contains("unsafe-eval", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "weak: allows unsafe-inline or unsafe-eval");
        }

        return (true, "sound");
    }

    private static (bool IsSound, string Verdict) GradeContentTypeOptions(Dictionary<string, string> values)
    {
        if (!values.TryGetValue("X-Content-Type-Options", out string? value))
        {
            return (false, "missing");
        }

        return string.Equals(value, "nosniff", StringComparison.OrdinalIgnoreCase)
            ? (true, "sound")
            : (false, $"weak: value is \"{value}\", expected nosniff");
    }

    private static (bool IsSound, string Verdict) GradeFrameProtection(Dictionary<string, string> values)
    {
        if (values.TryGetValue("Content-Security-Policy", out string? csp) && csp.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase))
        {
            return (true, "sound (frame-ancestors in the Content-Security-Policy)");
        }

        if (!values.TryGetValue("X-Frame-Options", out string? value))
        {
            return (false, "missing");
        }

        string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 1 && (string.Equals(words[0], "DENY", StringComparison.OrdinalIgnoreCase) || string.Equals(words[0], "SAMEORIGIN", StringComparison.OrdinalIgnoreCase))
            ? (true, "sound")
            : (false, $"weak: value is \"{value}\", expected DENY or SAMEORIGIN");
    }

    private static (bool IsSound, string Verdict) GradeReferrerPolicy(Dictionary<string, string> values)
    {
        if (!values.TryGetValue("Referrer-Policy", out string? value))
        {
            return (false, "missing");
        }

        // A policy can list fallbacks separated by commas; the browser uses the last one it understands, so the last one decides.
        string last = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        return WeakReferrerPolicies.Contains(last, StringComparer.OrdinalIgnoreCase)
            ? (false, $"weak: \"{last}\" sends more of the address than needed")
            : (true, "sound");
    }
}
