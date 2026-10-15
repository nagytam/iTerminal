using iConsole.Data;
using iTerminal.Core.Model;

namespace iTerminal.Core.Test.Model;

[TestClass]
public class FolderTest
{
    [TestMethod]
    public void Constructor_WithoutParent_CreatesRootFolder()
    {
        var folder = new Folder(null, "My Computer");
        Assert.IsNotNull(folder);
        Assert.AreEqual("My Computer", folder.Name);
        Assert.IsNull(folder.Parent);
        Assert.IsNull(folder.Url);
    }

    [TestMethod]
    public void Constructor_WithoutParent_CreatesUrlForNonRootFolder()
    {
        var folder = new Folder(null, "C:\\");
        Assert.IsNotNull(folder);
        Assert.AreEqual("C:\\", folder.Name);
        Assert.IsNotNull(folder.Url);
        // Url may have file:// scheme prefix
        Assert.IsTrue(((string)folder.Url).Contains("C:\\"));
    }

    [TestMethod]
    public void Constructor_WithParent_CreatesChildFolder()
    {
        var parent = new Folder(null, "C:\\");
        var child = new Folder(parent, "Users");

        Assert.AreEqual(parent, child.Parent);
        Assert.AreEqual("Users", child.Name);
        Assert.IsNotNull(child.Url);
    }

    [TestMethod]
    public void Constructor_MultipleChildren_HaveSameParent()
    {
        var parent = new Folder(null, "C:\\");
        var child1 = new Folder(parent, "Users");
        var child2 = new Folder(parent, "Windows");

        Assert.AreEqual(parent, child1.Parent);
        Assert.AreEqual(parent, child2.Parent);
        Assert.AreEqual("Users", child1.Name);
        Assert.AreEqual("Windows", child2.Name);
    }

    [TestMethod]
    public void Constructor_NestedFolders_FormHierarchy()
    {
        var root = new Folder(null, "C:\\");
        var level1 = new Folder(root, "Users");
        var level2 = new Folder(level1, "MyUser");
        var level3 = new Folder(level2, "Documents");

        Assert.AreEqual(root, level1.Parent);
        Assert.AreEqual(level1, level2.Parent);
        Assert.AreEqual(level2, level3.Parent);
    }

    [TestMethod]
    public void Url_IsNotNullForRegularFolder()
    {
        var folder = new Folder(null, "/home/user");
        Assert.IsNotNull(folder.Url);
    }

    [TestMethod]
    public void Name_IsSetCorrectly()
    {
        var testNames = new[] { "Documents", "Downloads", "Desktop", "Projects" };

        foreach (var name in testNames)
        {
            var folder = new Folder(null, name);
            Assert.AreEqual(name, folder.Name);
        }
    }

    [TestMethod]
    public void Constructor_WithSpecialFolderNames()
    {
        var specialNames = new[] { "My Computer", ".", "..", "~" };

        foreach (var name in specialNames)
        {
            var folder = new Folder(null, name);
            Assert.AreEqual(name, folder.Name);

            if (name == "My Computer")
            {
                Assert.IsNull(folder.Url);
            }
            else
            {
                Assert.IsNotNull(folder.Url);
            }
        }
    }

    [TestMethod]
    public void ChildFolders_CanHaveDifferentUrls()
    {
        var parent = new Folder(null, "C:\\");
        var child1 = new Folder(parent, "Users");
        var child2 = new Folder(parent, "Windows");

        Assert.IsNotNull(child1.Url);
        Assert.IsNotNull(child2.Url);
        Assert.AreNotEqual((string)child1.Url, (string)child2.Url);
    }
}
