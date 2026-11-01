using iTerminal.Core.Services.Mail;

namespace iTerminal.Core.Test.Services.Mail;

/// <summary>The BER encoding an LDAP reply is read from: integers of every width and sign, long lengths, and replies cut short.</summary>
[TestClass]
public class BerTest
{
    [TestMethod]
    public void IntegersRoundTripAcrossEverySignAndWidth()
    {
        long[] values = [0, 1, 127, 128, 255, 256, 32767, 32768, -1, -128, -129, int.MaxValue, long.MinValue, long.MaxValue];
        foreach (long value in values)
        {
            var (tag, content) = Ber.Elements(Ber.Int(value))[0];

            Assert.AreEqual(Ber.Integer, tag);
            Assert.AreEqual(value, Ber.DecodeInt(content), value.ToString());
        }
    }

    [TestMethod]
    public void IntegersUseTheShortestTwosComplementForm()
    {
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, 0x00 }, Ber.Int(0));
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x02, 0x00, 0x80 }, Ber.Int(128), "a leading zero keeps 128 positive");
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, 0xFF }, Ber.Int(-1));
    }

    [TestMethod]
    public void ALengthOfMoreThan127BytesUsesTheLongForm()
    {
        byte[] encoded = Ber.Str(new string('x', 300));

        Assert.AreEqual(Ber.OctetString, encoded[0]);
        CollectionAssert.AreEqual(new byte[] { 0x82, 0x01, 0x2C }, encoded[1..4], "300 bytes after a two-byte length");
        var element = Ber.Elements(encoded);
        Assert.AreEqual(1, element.Count);
        Assert.AreEqual(300, element[0].Value.Length);
    }

    [TestMethod]
    public void ElementsReadsConsecutiveElementsInOrder()
    {
        byte[] data = Ber.Concat(Ber.Str("uid"), Ber.Bool(true), Ber.Int(7, Ber.Enumerated));

        var elements = Ber.Elements(data);

        Assert.AreEqual(3, elements.Count);
        Assert.AreEqual(Ber.OctetString, elements[0].Tag);
        Assert.AreEqual("uid", Ber.DecodeString(elements[0].Value));
        Assert.AreEqual(Ber.Boolean, elements[1].Tag);
        Assert.AreEqual(0xFF, elements[1].Value[0]);
        Assert.AreEqual(Ber.Enumerated, elements[2].Tag);
        Assert.AreEqual(7, Ber.DecodeInt(elements[2].Value));
    }

    [TestMethod]
    public void ANullOrEmptyValueHasNoElementsAndDecodesToZero()
    {
        Assert.AreEqual(0, Ber.Elements(null).Count);
        Assert.AreEqual(0, Ber.Elements([]).Count);
        Assert.AreEqual(0, Ber.DecodeInt(null));
        Assert.AreEqual(0, Ber.DecodeInt([]));
    }

    [TestMethod]
    public void AnElementThatRunsPastTheEndIsAFormatError()
    {
        // Tag 0x04, a length of five, then only one byte of content.
        Assert.ThrowsExactly<FormatException>(() => Ber.Elements([0x04, 0x05, 0x01]));
    }

    [TestMethod]
    public void AnElementWithNoLengthIsAFormatError()
    {
        Assert.ThrowsExactly<FormatException>(() => Ber.Elements([0x04]));
    }

    [TestMethod]
    public void ALengthLongerThanFourBytesIsRefused()
    {
        Assert.ThrowsExactly<FormatException>(() => Ber.Elements([0x04, 0x85, 0x00, 0x00, 0x00, 0x00, 0x01]));
    }

    [TestMethod]
    public void StringsRoundTripAsUtf8()
    {
        const string text = "Árvíztűrő tükörfúrógép";

        Assert.AreEqual(text, Ber.DecodeString(Ber.Elements(Ber.Str(text))[0].Value));
    }

    [TestMethod]
    public async Task ReadElementAsyncReadsOneWholeElementWithItsLongLength()
    {
        byte[] first = Ber.Str(new string('y', 200));
        byte[] second = Ber.Int(42);
        await using MemoryStream stream = new(Ber.Concat(first, second));

        var (tag, value) = await Ber.ReadElementAsync(stream, CancellationToken.None);

        Assert.AreEqual(Ber.OctetString, tag);
        Assert.AreEqual(200, value.Length);
        Assert.AreEqual(first.Length + second.Length, stream.Length, "the stream is positioned for the next element, not read to the end");
        Assert.AreEqual(first.Length, stream.Position);
    }
}
