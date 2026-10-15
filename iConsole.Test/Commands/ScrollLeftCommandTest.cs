using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;

namespace iConsole.Commands.Test;

[TestClass]
public class ScrollLeftCommandTest
{
    [TestMethod]
    public void TypeExists() => Assert.IsNotNull(typeof(ScrollLeftCommand));
}

