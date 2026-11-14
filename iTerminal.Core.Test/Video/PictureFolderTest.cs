using iTerminal.Core.Video;

namespace iTerminal.Core.Test.Video;

[TestClass]
public class PictureFolderTest
{
    [TestMethod]
    public void PicturesBeside_ListsOnlyPicturesByName()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string name in new[] { "b.png", "A.jpg", "c.txt", "d.psd", "e.cr2" })
            {
                File.WriteAllBytes(Path.Combine(folder, name), []);
            }

            IReadOnlyList<string> pictures = PictureFolder.PicturesBeside(Path.Combine(folder, "b.png"));

            CollectionAssert.AreEqual(
                new[] { "A.jpg", "b.png", "d.psd", "e.cr2" },
                pictures.Select(Path.GetFileName).ToArray(),
                "by name, without case, and no text file");
            Assert.AreEqual(1, PictureFolder.IndexOf(pictures, Path.Combine(folder, "b.png")));
            Assert.AreEqual(-1, PictureFolder.IndexOf(pictures, Path.Combine(folder, "c.txt")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void PicturesBeside_IsEmptyForAFolderThatDoesNotExist()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "photo.png");

        Assert.IsEmpty(PictureFolder.PicturesBeside(missing));
    }
}
