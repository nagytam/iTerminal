using System.Reflection;

namespace iConsole.Inputs.Windows.Test;

[TestClass]
public class NativeMethodsTest
{
    [TestMethod]
    public void NativeMethods_DefinesExpectedConstantsAndPInvokeMembers()
    {
        Type nativeMethodsType = typeof(iConsole.Inputs.Windows.Extensions.BuilderExtension).Assembly
            .GetType("iConsole.Inputs.Windows.Jobs.NativeMethods", throwOnError: true)!;

        Assert.AreEqual(-10, (int)nativeMethodsType.GetField("STD_INPUT_HANDLE", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!);
        Assert.IsNotNull(nativeMethodsType.GetMethod("GetConsoleMode", BindingFlags.Public | BindingFlags.Static));
        Assert.IsNotNull(nativeMethodsType.GetMethod("GetStdHandle", BindingFlags.Public | BindingFlags.Static));
        Assert.IsNotNull(nativeMethodsType.GetMethod("ReadConsoleInput", BindingFlags.Public | BindingFlags.Static));
        Assert.IsNotNull(nativeMethodsType.GetMethod("SetConsoleMode", BindingFlags.Public | BindingFlags.Static));
    }
}
