using iTerminal.Core.Model.Calendar;
using iTerminal.Core.Model.ToDo;

namespace iTerminal.Core.Test.Model;

/// <summary>What people type into the meeting and task dialogs must never throw, whatever it is.</summary>
[TestClass]
public class ParserFuzzGuardTest
{
    private static IEnumerable<string> Inputs()
    {
        string[] fixedInputs =
        [
            "", " ", "\t", "!", "@", "!@", "@2026-02-30", "@9999-12-31", "! @2026-11-01", "!!!", "@@@@",
            "99:99 x", "24:00 x", "23:59 1 x", "23:59 2000000000 x", "00:00 0 x", "00:00 -5 x",
            "14:30 99999999999999999999 x", "14:30 ١٢ x", "14:30 ‮ x", "1:5 x", "14:30:00 x",
            new string('x', 100_000), "14:30 " + new string('y', 100_000), "!" + new string('@', 5000),
        ];
        foreach (string text in fixedInputs)
        {
            yield return text;
        }

        Random random = new(41);
        const string Alphabet = "0123456789:!@- xXé١";
        for (int i = 0; i < 3000; i++)
        {
            char[] chars = new char[random.Next(0, 24)];
            for (int c = 0; c < chars.Length; c++)
            {
                chars[c] = Alphabet[random.Next(Alphabet.Length)];
            }

            yield return new string(chars);
        }
    }

    [TestMethod]
    public void MeetingTryParse_NeverThrows_AndExplainsEveryRefusal()
    {
        foreach (string text in Inputs())
        {
            bool ok = Meeting.TryParse(text, new DateOnly(2026, 1, 1), out Meeting? meeting, out string error);

            if (ok)
            {
                Assert.IsNotNull(meeting);
                Assert.IsTrue(meeting.DurationMinutes > 0);
                Assert.IsTrue(meeting.Start.ToTimeSpan().TotalMinutes + meeting.DurationMinutes <= 24 * 60);
                Assert.IsFalse(string.IsNullOrWhiteSpace(meeting.Title));
                Assert.IsFalse(string.IsNullOrEmpty(meeting.Describe()));
            }
            else
            {
                Assert.IsNull(meeting);
                Assert.IsFalse(string.IsNullOrWhiteSpace(error), $"No explanation for \"{text}\"");
            }
        }
    }

    [TestMethod]
    public void ToDoTaskTryParse_NeverThrows_AndExplainsEveryRefusal()
    {
        foreach (string text in Inputs())
        {
            bool ok = ToDoTask.TryParse(text, Guid.NewGuid(), out ToDoTask? task, out string error);

            if (ok)
            {
                Assert.IsNotNull(task);
                Assert.IsFalse(string.IsNullOrWhiteSpace(task.Subject));
                Assert.IsFalse(task.IsComplete);
            }
            else
            {
                Assert.IsNull(task);
                Assert.IsFalse(string.IsNullOrWhiteSpace(error), $"No explanation for \"{text}\"");
            }
        }
    }
}
