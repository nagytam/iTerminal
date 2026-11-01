using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;

namespace iConsole.Commands.Test;

[TestClass]
public class ScrollUpCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TypeExists() => Assert.IsNotNull(typeof(ScrollUpCommand));
}

