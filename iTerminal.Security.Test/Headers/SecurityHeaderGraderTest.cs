using iTerminal.Security.Headers;

namespace iTerminal.Security.Test.Headers;

[TestClass]
public class SecurityHeaderGraderTest
{
    private static KeyValuePair<string, string> H(string name, string value) => new(name, value);

    private static readonly KeyValuePair<string, string>[] Hardened =
    [
        H("Strict-Transport-Security", "max-age=31536000; includeSubDomains"),
        H("Content-Security-Policy", "default-src 'self'"),
        H("X-Content-Type-Options", "nosniff"),
        H("X-Frame-Options", "DENY"),
        H("Referrer-Policy", "strict-origin-when-cross-origin"),
    ];

    [TestMethod]
    public void Grade_AllFiveCoreHeadersSound_IsA()
    {
        SecurityHeaderGrade grade = SecurityHeaderGrader.Grade(Hardened);

        Assert.AreEqual("A", grade.Letter);
        Assert.AreEqual(5, grade.Sound);
        StringAssert.StartsWith(grade.Lines[0], "Grade: A (5 of 5");
    }

    [TestMethod]
    public void Grade_NoHeaders_IsFAndEveryCoreHeaderIsMissing()
    {
        SecurityHeaderGrade grade = SecurityHeaderGrader.Grade([]);

        Assert.AreEqual("F", grade.Letter);
        Assert.AreEqual(0, grade.Sound);
        CollectionAssert.Contains(grade.Lines.ToList(), "Content-Security-Policy: missing");
        CollectionAssert.Contains(grade.Lines.ToList(), "Strict-Transport-Security: missing (only meaningful over HTTPS)");
        CollectionAssert.Contains(grade.Lines.ToList(), "Permissions-Policy: not sent (recommended, not counted in the grade)");
    }

    [TestMethod]
    public void Grade_LettersFollowTheCountOfSoundHeaders()
    {
        Assert.AreEqual("B", SecurityHeaderGrader.Grade(Hardened.Take(4)).Letter);
        Assert.AreEqual("C", SecurityHeaderGrader.Grade(Hardened.Take(3)).Letter);
        Assert.AreEqual("D", SecurityHeaderGrader.Grade(Hardened.Take(2)).Letter);
        Assert.AreEqual("F", SecurityHeaderGrader.Grade(Hardened.Take(1)).Letter);
    }

    [TestMethod]
    public void Hsts_ShortMaxAge_IsWeakAndNamesTheAge()
    {
        SecurityHeaderGrade grade = SecurityHeaderGrader.Grade([H("Strict-Transport-Security", "max-age=3600")]);

        CollectionAssert.Contains(grade.Lines.ToList(), "Strict-Transport-Security: weak: max-age is 3600 seconds, below one year");
    }

    [TestMethod]
    public void Hsts_WithoutMaxAge_IsWeak()
    {
        SecurityHeaderGrade grade = SecurityHeaderGrader.Grade([H("Strict-Transport-Security", "includeSubDomains")]);

        CollectionAssert.Contains(grade.Lines.ToList(), "Strict-Transport-Security: weak: no max-age");
    }

    [TestMethod]
    public void Csp_WithUnsafeInline_IsWeak()
    {
        SecurityHeaderGrade grade = SecurityHeaderGrader.Grade([H("Content-Security-Policy", "script-src 'self' 'unsafe-inline'")]);

        Assert.AreEqual(0, grade.Sound);
        CollectionAssert.Contains(grade.Lines.ToList(), "Content-Security-Policy: weak: allows unsafe-inline or unsafe-eval");
    }

    [TestMethod]
    public void FrameProtection_FrameAncestorsInCspCountsInPlaceOfXFrameOptions()
    {
        SecurityHeaderGrade grade = SecurityHeaderGrader.Grade([H("Content-Security-Policy", "default-src 'self'; frame-ancestors 'none'")]);

        CollectionAssert.Contains(grade.Lines.ToList(), "X-Frame-Options / frame-ancestors: sound (frame-ancestors in the Content-Security-Policy)");
    }

    [TestMethod]
    public void FrameProtection_XFrameOptionsAllowFrom_IsWeak()
    {
        SecurityHeaderGrade grade = SecurityHeaderGrader.Grade([H("X-Frame-Options", "ALLOW-FROM https://example.com")]);

        Assert.AreEqual(0, grade.Sound);
        StringAssert.Contains(grade.Lines.First(line => line.StartsWith("X-Frame-Options", StringComparison.Ordinal)), "weak: value is");
    }

    [TestMethod]
    public void ReferrerPolicy_UsesTheLastFallbackAndFlagsUnsafeUrl()
    {
        SecurityHeaderGrade weak = SecurityHeaderGrader.Grade([H("Referrer-Policy", "no-referrer, unsafe-url")]);
        SecurityHeaderGrade sound = SecurityHeaderGrader.Grade([H("Referrer-Policy", "unsafe-url, strict-origin")]);

        StringAssert.Contains(weak.Lines.First(line => line.StartsWith("Referrer-Policy", StringComparison.Ordinal)), "weak");
        StringAssert.Contains(sound.Lines.First(line => line.StartsWith("Referrer-Policy", StringComparison.Ordinal)), "sound");
    }

    [TestMethod]
    public void HeaderNames_MatchWithoutCase()
    {
        SecurityHeaderGrade grade = SecurityHeaderGrader.Grade([H("x-content-type-options", "nosniff")]);

        Assert.AreEqual(1, grade.Sound);
        CollectionAssert.Contains(grade.Lines.ToList(), "X-Content-Type-Options: sound");
    }

    [TestMethod]
    public void Server_AndXPoweredBy_AreFlaggedForRemoval()
    {
        SecurityHeaderGrade grade = SecurityHeaderGrader.Grade([H("Server", "nginx/1.25"), H("X-Powered-By", "PHP/8.3")]);

        CollectionAssert.Contains(grade.Lines.ToList(), "Server: nginx/1.25 (remove it: it tells an attacker which software runs here)");
        CollectionAssert.Contains(grade.Lines.ToList(), "X-Powered-By: PHP/8.3 (remove it: it tells an attacker which software runs here)");
    }

    [TestMethod]
    public void ExpectCt_AndPublicKeyPins_AreFlaggedAsDeprecated()
    {
        SecurityHeaderGrade grade = SecurityHeaderGrader.Grade([H("Expect-CT", "max-age=86400"), H("Public-Key-Pins", "pin-sha256=\"x\"")]);

        CollectionAssert.Contains(grade.Lines.ToList(), "Expect-CT: deprecated; browsers no longer act on it");
        CollectionAssert.Contains(grade.Lines.ToList(), "Public-Key-Pins: deprecated; browsers no longer act on it");
    }

    [TestMethod]
    public void Grade_IsAHintAboutOneResponse()
    {
        SecurityHeaderGrade grade = SecurityHeaderGrader.Grade(Hardened);

        StringAssert.Contains(grade.Lines[0], "a hint about this response only");
    }
}
