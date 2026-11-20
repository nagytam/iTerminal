using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Model;

namespace iTerminal.Core.Model.Test;

/// <summary>The "My Computer" root of the folder index: named, open and focused from the start, so the tree is never a closed root.</summary>
[TestClass]
public class FolderIndexModelTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TheRoot_IsMyComputer_AndStartsOpenAndFocused()
    {
        var root = new FolderIndexModel();

        Assert.AreEqual("My Computer", root.Name);
        Assert.IsTrue(root.IsOpened);
        Assert.IsTrue(root.IsFocused);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsWalking_RaisesPropertyChangedOnlyWhenItChanges()
    {
        var root = new FolderIndexModel();
        var raised = new List<string?>();
        root.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        root.IsWalking = true;
        root.IsWalking = true;
        root.IsWalking = false;

        CollectionAssert.AreEqual(new[] { nameof(FolderIndexModel.IsWalking), nameof(FolderIndexModel.IsWalking) }, raised);
        Assert.IsFalse(root.IsWalking);
    }
}
