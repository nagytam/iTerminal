using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;

namespace iConsole.Commands.Test;

[TestClass]
public class TabForwardCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TypeExists() => Assert.IsNotNull(typeof(TabForwardCommand));
}

