using iTerminal.Core.Services.Shell;
using iTerminal.Core.Services.Elevation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Services.Elevation.Test;

/// <summary>Only a program that exists can be run elevated; on Unix it also has to be executable.</summary>
[TestClass]
public class ElevationServiceTest
{
    private static ElevationService NewService() => new(Mock.Of<IInteractiveShellService>());

    [Timeout(60_000)]
    [TestMethod]
    public void ABlankPath_CannotRunElevated()
    {
        Assert.IsFalse(NewService().CanRunElevated(string.Empty));
        Assert.IsFalse(NewService().CanRunElevated("   "));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMissingFile_CannotRunElevated()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-program-" + Guid.NewGuid().ToString("N"));

        Assert.IsFalse(NewService().CanRunElevated(missing));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnUnix_ANonExecutableFile_CannotRunElevated()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The execute-bit rule is Unix's.");
        }

        var file = Path.Combine(Path.GetTempPath(), "plain-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "not a program");
        try
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            Assert.IsFalse(NewService().CanRunElevated(file));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
