using iConsole.Inputs.Windows.Data;

namespace iConsole.Inputs.Windows.Test.Data;

[TestClass]
public class ConsoleHandleTest
{
    private sealed class TestConsoleHandle : ConsoleHandle
    {
        public void SetTestHandle(IntPtr handle)
        {
            SetHandle(handle);
        }

        public bool ReleaseForTest()
        {
            return ReleaseHandle();
        }
    }

    [TestMethod]
    public void ReleaseHandle_ReturnsTrue()
    {
        TestConsoleHandle handle = new();
        handle.SetTestHandle(new IntPtr(123));

        bool released = handle.ReleaseForTest();

        Assert.IsTrue(released);
    }
}
