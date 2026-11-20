using iConsole.Qr;

namespace iConsole.Test.Qr;

[TestClass]
public class QrEncoderTest
{
    // The published worked example: the 16 data codewords of version 1-M (the alphanumeric
    // encoding of HELLO WORLD) and the 10 check codewords that follow them.
    private static readonly byte[] HelloWorldData = [32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17];
    private static readonly byte[] HelloWorldCheck = [196, 35, 39, 119, 235, 215, 231, 226, 93, 23];

    private static readonly string[] FormatStrings =
    [
        "101010000010010",
        "101000100100101",
        "101111001111100",
        "101101101001011",
        "100010111111001",
        "100000011001110",
        "100111110010111",
        "100101010100000",
    ];

    [TestMethod]
    public void ReedSolomon_OfPublishedExample_GivesPublishedCheckCodewords()
    {
        CollectionAssert.AreEqual(HelloWorldCheck, QrEncoder.ReedSolomonRemainder(HelloWorldData, 10));
    }

    [TestMethod]
    public void FormatBits_ForLevelM_MatchTheStandardTableForEveryMask()
    {
        for (int mask = 0; mask < 8; mask++)
        {
            string bits = Convert.ToString(QrEncoder.FormatBits(mask), 2).PadLeft(15, '0');

            Assert.AreEqual(FormatStrings[mask], bits, $"mask {mask}");
        }
    }

    [TestMethod]
    public void VersionBits_ForVersion7_MatchTheStandardValue()
    {
        Assert.AreEqual(0x07C94, QrEncoder.VersionBits(7));
    }

    [TestMethod]
    public void VersionFor_UsesTheStandardLevelMCapacities()
    {
        Assert.AreEqual(14, QrEncoder.ByteCapacity(1));
        Assert.AreEqual(26, QrEncoder.ByteCapacity(2));
        Assert.AreEqual(213, QrEncoder.ByteCapacity(10));

        Assert.AreEqual(1, QrEncoder.VersionFor(14));
        Assert.AreEqual(2, QrEncoder.VersionFor(15));
        Assert.AreEqual(10, QrEncoder.VersionFor(213));
    }

    [TestMethod]
    public void Encode_InputLongerThanVersion10_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => QrEncoder.Encode(new byte[QrEncoder.MaxBytes + 1]));
    }

    [TestMethod]
    public void Encode_Version1_HasFinderTimingAndDarkModulesAtTheStandardPositions()
    {
        bool[,] modules = QrEncoder.Encode("A"u8.ToArray());

        Assert.AreEqual(21, modules.GetLength(0));
        Assert.IsTrue(modules[3, 3], "finder centre");
        Assert.IsFalse(modules[1, 1], "finder inner ring");
        Assert.IsTrue(modules[0, 0], "finder outer corner");
        Assert.IsTrue(modules[8, 13], "dark module");
        for (int i = 8; i <= 12; i++)
        {
            Assert.AreEqual(i % 2 == 0, modules[i, 6], $"horizontal timing at {i}");
        }
    }

    [TestMethod]
    public void Encode_Version2_HasAnAlignmentPatternCentredAt18()
    {
        bool[,] modules = QrEncoder.Encode(new byte[20]);

        Assert.AreEqual(25, modules.GetLength(0));
        Assert.IsTrue(modules[18, 18], "alignment centre");
        Assert.IsFalse(modules[17, 18], "alignment inner ring");
        Assert.IsTrue(modules[16, 18], "alignment outer ring");
    }

    [TestMethod]
    public void Penalty_OfARunOfFive_ScoresRuleN1()
    {
        // One row of six light modules and a single dark module: the run of six scores 3 + (6 - 5) = 4
        // on its row and column; the rest is a single dark module, which scores nothing else.
        bool[,] modules = new bool[6, 6];
        for (int x = 0; x < 6; x++)
        {
            modules[x, 0] = false;
        }

        int penalty = QrEncoder.Penalty(modules);

        Assert.IsTrue(penalty >= 4, $"run of six must score at least 4, got {penalty}");
    }

    [TestMethod]
    public void Encode_SameInput_GivesTheSameMatrix()
    {
        bool[,] first = QrEncoder.Encode("https://192.168.1.20:8080/token"u8.ToArray());
        bool[,] second = QrEncoder.Encode("https://192.168.1.20:8080/token"u8.ToArray());

        CollectionAssert.AreEqual(first, second);
    }
}
