using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class ProcessServiceTest
{
    [TestMethod]
    public void ProcessServiceConstructor()
    {
        var service = new ProcessService();
        Assert.IsNotNull(service);
        Assert.IsNotNull(service.Processes);
    }

    [TestMethod]
    public void ProcessServiceProcessesNotEmpty()
    {
        var service = new ProcessService();
        Assert.IsTrue(service.Processes.Count > 0);
    }

    [TestMethod]
    public void ProcessServiceProcessesSorted()
    {
        var service = new ProcessService();
        for (int i = 0; i < service.Processes.Count - 1; i++)
        {
            var current = service.Processes[i];
            var next = service.Processes[i + 1];
            Assert.IsTrue(current.Id <= next.Id);
        }
    }

    [TestMethod]
    public void ProcessServiceContainsCurrentProcess()
    {
        var service = new ProcessService();
        var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;

        var found = false;
        foreach (var process in service.Processes)
        {
            if (process.Id == currentProcessId)
            {
                found = true;
                break;
            }
        }
        Assert.IsTrue(found);
    }

    [TestMethod]
    public void ProcessServiceProcessesAreValid()
    {
        var service = new ProcessService();
        foreach (var process in service.Processes)
        {
            Assert.IsTrue(process.Id >= 0);
        }
    }
}
