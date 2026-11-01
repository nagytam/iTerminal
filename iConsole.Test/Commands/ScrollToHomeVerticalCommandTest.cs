using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;

namespace iConsole.Commands.Test;

[TestClass]
public class ScrollToHomeVerticalCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TypeExists() => Assert.IsNotNull(typeof(ScrollToHomeVerticalCommand));
}

