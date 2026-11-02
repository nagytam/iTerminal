using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iTerminal.Core.Model.ContentModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Model.ContentModels;

/// <summary>The Long listing's attributes text, and the Long row's columns.</summary>
[TestClass]
public class EntryListingAttributesTest
{
    [TestMethod]
    public void FormatAttributes_ReadsTheUnixPermissionBits()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix permission bits are read on Linux and macOS only.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"listing-mode-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "x");
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            var text = EntryBase.FormatAttributes(new Url(new FileInfo(path)));

            Assert.AreEqual("rw-r--r--", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void FormatAttributes_OfAPathStringIsBlank()
    {
        var text = EntryBase.FormatAttributes(new Url(Path.GetTempPath()));

        Assert.AreEqual(new string(' ', EntryBase.AttributeLength), text);
    }

    [TestMethod]
    public void FormatLongItemName_HasTheAttributesAndNoSizeColumn()
    {
        var path = Path.Combine(Path.GetTempPath(), $"listing-long-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "hello");
        try
        {
            var entry = new FileSystemEntry(new FileInfo(path));
            var row = entry.FormatLongItemName(70, false, new ColorScheme(), VisualState.Normal);
            var text = string.Concat(row.String.Select(cell => cell.Char));

            Assert.AreEqual(70, text.Length, "the row fills its width");
            Assert.AreEqual(3, (text.Length - text.Replace(Border.VerticalSingle, string.Empty).Length) / Border.VerticalSingle.Length, "name | attributes | date | time: three separators");
            Assert.IsTrue(text.Contains(EntryBase.FormatAttributes(entry.Url)), "the attributes column is drawn");
            Assert.IsFalse(text.Contains(entry.FileSize), "the size column is not drawn in Long");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
