using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Jobs;
using iConsole.Data;

namespace iConsole.Jobs.Test;

[TestClass]
public class KeyboardEventPublisherJobTest
{
    [TestMethod]
    public void EnqueueAndEvent_ShouldWorkWhenInvokedManually()
    {
        var job = new KeyboardEventPublisherJob();

        var evt = new KeyEvent('a', ConsoleKey.A, false, false, false);
        job.KeyQueue.Enqueue(evt);

        Assert.IsTrue(job.KeyQueue.TryDequeue(out var dequeued));
        Assert.AreEqual('a', dequeued.KeyChar);

        bool invoked = false;
        job.KeyEvent += (s, e) => invoked = true;
        // simulate publisher raising the event via reflection (invoke backing delegate)
        var field = job.GetType().GetField("KeyEvent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var dlg = field?.GetValue(job) as System.MulticastDelegate;
        dlg?.DynamicInvoke(job, evt);
        Assert.IsTrue(invoked);
    }
}

