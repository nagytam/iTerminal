using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;

namespace iConsole.Commands.Test;

[TestClass]
public class CancelDialogCommandTest
{
    [TestMethod]
    public void TypeExists()
    {
        Assert.IsNotNull(typeof(CancelDialogCommand));
    }
}

