using iTerminal.Core.Helper;

namespace iTerminal.Core.Test.Helper;

[TestClass]
public class StreamHelperTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_WithSimpleString_CreatesMemoryStream()
    {
        var input = "Hello World";
        var stream = input.ToStream();

        Assert.IsNotNull(stream);
        Assert.IsInstanceOfType(stream, typeof(MemoryStream));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_WithSimpleString_CanBeReadBack()
    {
        var input = "Hello World";
        var stream = input.ToStream();

        using var reader = new StreamReader(stream);
        var output = reader.ReadToEnd();

        Assert.AreEqual(input, output);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_WithEmptyString_CreatesStream()
    {
        var input = string.Empty;
        var stream = input.ToStream();

        Assert.IsNotNull(stream);
        using var reader = new StreamReader(stream);
        var output = reader.ReadToEnd();
        Assert.AreEqual(string.Empty, output);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_WithMultilineString_PreservesContent()
    {
        var input = "Line 1\nLine 2\nLine 3";
        var stream = input.ToStream();

        using var reader = new StreamReader(stream);
        var output = reader.ReadToEnd();

        Assert.AreEqual(input, output);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_WithSpecialCharacters_PreservesContent()
    {
        var input = "Special chars: !@#$%^&*()_+-=[]{}|;':\",./<>?";
        var stream = input.ToStream();

        using var reader = new StreamReader(stream);
        var output = reader.ReadToEnd();

        Assert.AreEqual(input, output);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_WithUnicodeCharacters_PreservesContent()
    {
        var input = "Unicode: 你好 مرحبا Привет";
        var stream = input.ToStream();

        using var reader = new StreamReader(stream);
        var output = reader.ReadToEnd();

        Assert.AreEqual(input, output);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_StreamPositionIsAtStart()
    {
        var input = "Test Stream";
        var stream = input.ToStream();

        Assert.AreEqual(0, stream.Position);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_StreamCanBeReadMultipleTimes()
    {
        var input = "Reusable Stream";
        var stream = input.ToStream();

        using var reader1 = new StreamReader(stream);
        var output1 = reader1.ReadToEnd();

        stream.Position = 0;

        using var reader2 = new StreamReader(stream);
        var output2 = reader2.ReadToEnd();

        Assert.AreEqual(output1, output2);
        Assert.AreEqual(input, output1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_WithLongString_HandlesCorrectly()
    {
        var input = new string('A', 10000);
        var stream = input.ToStream();

        using var reader = new StreamReader(stream);
        var output = reader.ReadToEnd();

        Assert.AreEqual(input.Length, output.Length);
        Assert.AreEqual(input, output);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_WithWhitespaceOnlyString()
    {
        var input = "   \t\n   ";
        var stream = input.ToStream();

        using var reader = new StreamReader(stream);
        var output = reader.ReadToEnd();

        Assert.AreEqual(input, output);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_StreamIsSeekable()
    {
        var input = "Seekable Stream";
        var stream = input.ToStream();

        Assert.IsTrue(stream.CanSeek);
        Assert.IsTrue(stream.CanRead);
        Assert.IsTrue(stream.CanWrite);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_StreamLengthMatchesStringLength()
    {
        var input = "Test";
        var stream = input.ToStream();

        // Stream length should match the byte length of the string
        Assert.AreEqual(input.Length, stream.Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_WithNumberString()
    {
        var input = "1234567890";
        var stream = input.ToStream();

        using var reader = new StreamReader(stream);
        var output = reader.ReadToEnd();

        Assert.AreEqual(input, output);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToStream_MultipleStreamsAreIndependent()
    {
        var input1 = "Stream 1";
        var input2 = "Stream 2";

        var stream1 = input1.ToStream();
        var stream2 = input2.ToStream();

        using var reader1 = new StreamReader(stream1);
        using var reader2 = new StreamReader(stream2);

        var output1 = reader1.ReadToEnd();
        var output2 = reader2.ReadToEnd();

        Assert.AreEqual(input1, output1);
        Assert.AreEqual(input2, output2);
    }
}
