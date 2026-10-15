using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Core;

public static class StringAssert
{
    public static void AreEqual(string s1, string s2)
    {
        var message = string.Empty;
        if (s1 != s2)
        {
            var lines1 = s1.Split(Environment.NewLine);
            var lines2 = s2.Split(Environment.NewLine);
            if (lines1.Length != lines2.Length)
            {
                Assert.Fail($"Number of lines are different: {lines1.Length} vs {lines2.Length}");
            }

            for (int i = 0; i<lines1.Length; i++)
            {
                if (lines1[i] != lines2[i])
                {
                    message = $"Line {i}:{Environment.NewLine}{lines1[i]}{Environment.NewLine}{lines2[i]}";
                    break;
                }
            }
        }
        Assert.AreEqual(s1, s2, message);
    }
}
