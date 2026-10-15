using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;

namespace iConsole.Commands.Test;

[TestClass]
public class DatePopUpWindowCommandTest
{
    [TestMethod]
    public void TypeExists()
    {
        Assert.IsNotNull(typeof(DatePopUpWindowCommand));
    }
}

