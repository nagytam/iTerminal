using iConsole.Data;

namespace iConsole.Test.Data;

/// <summary>Names the URL parser has to keep whole: an '@' that is not an archive separator, blanks, accents, hyphenated hosts.</summary>
[TestClass]
public class UrlSpecialCharactersTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void LocalFileWithAtSignInNameIsAFile()
    {
        Url url = new(Path.Combine(Path.GetTempPath(), "photo@2x.png"));

        Assert.AreEqual(ContainerType.FileSystem, url.ContainerType);
        Assert.AreEqual("photo@2x.png", url.Name);
        Assert.AreEqual("png", url.Extension);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LocalFolderWithAtSignIsAFolder()
    {
        Url url = new(Path.Combine(Path.GetTempPath(), "team@home") + Path.DirectorySeparatorChar);

        Assert.AreEqual(ContainerType.FileSystem, url.ContainerType);
        Assert.AreEqual(PathType.Folder, url.PathType);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SshHostWithHyphenAndUserWithDotAreParsed()
    {
        Url url = new("ssh://first.last@my-server:/home/first/a b.txt");

        Assert.AreEqual(ContainerType.SshFileSystem, url.ContainerType);
        Assert.AreEqual("my-server", url.Host);
        Assert.AreEqual("first.last", url.Username);
        Assert.AreEqual("/home/first/a b.txt", url.Path);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SshPathKeepsNonAsciiCharacters()
    {
        Url url = new("ssh://user@host:/home/user/árvíztűrő.txt");

        Assert.AreEqual("/home/user/árvíztűrő.txt", url.Path);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SmbPathKeepsBlanksAndNonAsciiCharacters()
    {
        Url url = new("smb://user@host/share/My Documents/ä.txt");

        Assert.AreEqual("host", url.Host);
        Assert.AreEqual("user", url.Username);
        Assert.AreEqual("share", url.Share);
        Assert.AreEqual("/share/My Documents/ä.txt", url.Path);
        Assert.AreEqual("ä.txt", url.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnonymousSmbPathKeepsBlanks()
    {
        Url url = new("smb://host/share/My Documents/");

        Assert.AreEqual("/share/My Documents/", url.Path);
        Assert.AreEqual(PathType.Folder, url.PathType);
    }
}
