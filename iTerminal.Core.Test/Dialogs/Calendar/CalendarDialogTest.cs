using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs.Calendar;
using iTerminal.Core.Model.Calendar;
using iTerminal.Core.Services;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Calendar;

// Some tests put text on the ambient clipboard and paste it back, so the class does not run beside others that do.
[DoNotParallelize]
[TestClass]
public class CalendarDialogTest
{
    private static readonly DateTime Now = new(2026, 10, 14, 10, 0, 0);

    private sealed class Fixture
    {
        public required CalendarDialog Dialog { get; init; }
        public required MeetingStore Meetings { get; init; }
    }

    private static Fixture Create(DateTime? now = null)
    {
        var dateTime = new Mock<IDateTimeProviderService>();
        dateTime.Setup(d => d.Now).Returns(now ?? Now);
        var settings = new Mock<ISettingsService>();
        var meetings = new MeetingStore(settings.Object);
        return new Fixture
        {
            Dialog = new CalendarDialog(dateTime.Object, meetings, TestTheme()),
            Meetings = meetings,
        };
    }

    private static async Task TypeAsync(CalendarDialog dialog, string text)
    {
        foreach (var c in text)
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }
    }

    [TestMethod]
    public async Task OpensOnToday()
    {
        var fixture = Create();

        await ShowAsync(fixture.Dialog);

        Assert.AreEqual(new DateOnly(2026, 10, 14), fixture.Dialog.CalendarForTest.SelectedDate);
    }

    [TestMethod]
    public async Task ArrowsMoveByDayAndWeekAndPageKeysByMonth()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(new DateOnly(2026, 10, 15), fixture.Dialog.CalendarForTest.SelectedDate);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(new DateOnly(2026, 10, 22), fixture.Dialog.CalendarForTest.SelectedDate);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(new DateOnly(2026, 11, 22), fixture.Dialog.CalendarForTest.SelectedDate);
    }

    [TestMethod]
    public async Task ReopeningStartsFromTodayAgain()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.PageDown));

        await fixture.Dialog.ShowAsync();

        Assert.AreEqual(new DateOnly(2026, 10, 14), fixture.Dialog.CalendarForTest.SelectedDate);
    }

    [TestMethod]
    public async Task ATypedMeetingIsAddedToTheSelectedDayAndDrawn()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await TypeAsync(fixture.Dialog, "09:00 30 Stand up");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        var meetings = fixture.Meetings.On(new DateOnly(2026, 10, 14));
        Assert.AreEqual(1, meetings.Count);
        Assert.AreEqual("Stand up", meetings[0].Title);
        Assert.IsNull(fixture.Dialog.EntryForTest);

        var screen = Render(fixture.Dialog);
        AssertScreenContains(screen, "09:00–09:30  Stand up");
    }

    [TestMethod]
    public async Task ArrowsDoNotMoveTheCalendarWhileAMeetingIsBeingTyped()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));

        Assert.AreEqual(new DateOnly(2026, 10, 14), fixture.Dialog.CalendarForTest.SelectedDate);
        Assert.IsNotNull(fixture.Dialog.EntryForTest);
    }

    [TestMethod]
    public async Task EscapeAbandonsTheEntryAndKeepsTheDialogOpen()
    {
        var fixture = Create();
        var root = await ShowAsync(fixture.Dialog);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await TypeAsync(fixture.Dialog, "10:00 Half done");

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsNull(fixture.Dialog.EntryForTest);
        Assert.AreEqual(0, fixture.Meetings.On(new DateOnly(2026, 10, 14)).Count);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(fixture.Dialog));
    }

    [TestMethod]
    public async Task EscapeWithNothingOpenClosesTheDialog()
    {
        var fixture = Create();
        var root = await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(fixture.Dialog));
    }

    [TestMethod]
    public async Task AnInvalidMeetingStaysInTheEntryWithItsReason()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await TypeAsync(fixture.Dialog, "25:00 Nope");

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.IsNotNull(fixture.Dialog.EntryForTest);
        Assert.IsFalse(string.IsNullOrEmpty(fixture.Dialog.EntryErrorForTest));
        Assert.AreEqual(0, fixture.Meetings.On(new DateOnly(2026, 10, 14)).Count);
    }

    [TestMethod]
    public async Task DeleteRemovesTheHighlightedMeeting()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        var day = new DateOnly(2026, 10, 14);
        fixture.Meetings.Add(new Meeting(day, new TimeOnly(9, 0), 30, "Keep"));
        fixture.Meetings.Add(new Meeting(day, new TimeOnly(11, 0), 30, "Remove"));

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow, shift: false, alt: true));
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        Assert.AreEqual(2, fixture.Meetings.On(day).Count, "Delete only asks");
        AssertScreenContains(Render(fixture.Dialog), "Delete \"Remove\"? (y/n)");
        await fixture.Dialog.HandleKeyEventAsync(Char('y'));

        CollectionAssert.AreEqual(new[] { "Keep" }, fixture.Meetings.On(day).Select(m => m.Title).ToArray());
    }

    [TestMethod]
    public async Task AnyKeyButYWithdrawsTheDeletion()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        var day = new DateOnly(2026, 10, 14);
        fixture.Meetings.Add(new Meeting(day, new TimeOnly(9, 0), 30, "Keep"));

        await fixture.Dialog.HandleKeyEventAsync(Char('d'));
        Assert.IsTrue(fixture.Dialog.ConfirmingDeleteForTest);
        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        Assert.IsFalse(fixture.Dialog.ConfirmingDeleteForTest);

        await fixture.Dialog.HandleKeyEventAsync(Char('d'));
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        Assert.AreEqual(1, fixture.Meetings.On(day).Count);
    }

    [TestMethod]
    public async Task DeleteOnADayWithoutMeetingsAsksNothing()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));

        Assert.IsFalse(fixture.Dialog.ConfirmingDeleteForTest);
    }

    [TestMethod]
    public async Task EPutsTheHighlightedMeetingInTheEntryLine_AndEnterReplacesIt()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        var day = new DateOnly(2026, 10, 14);
        fixture.Meetings.Add(new Meeting(day, new TimeOnly(9, 30), 45, "Standup"));

        await fixture.Dialog.HandleKeyEventAsync(Char('e'));
        Assert.AreEqual("09:30 45 Standup", fixture.Dialog.EntryForTest);
        AssertScreenContains(Render(fixture.Dialog), "Change meeting: 09:30 45 Standup");
        for (int i = 0; i < "Standup".Length; i++)
        {
            await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace));
        }

        await TypeAsync(fixture.Dialog, "Retro");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        var meetings = fixture.Meetings.On(day);
        Assert.AreEqual(1, meetings.Count);
        Assert.AreEqual("Retro", meetings[0].Title);
        Assert.AreEqual(45, meetings[0].DurationMinutes);
        Assert.IsNull(fixture.Dialog.EntryForTest);
    }

    [TestMethod]
    public async Task ARefusedChange_LosesNothing_AndEscapeKeepsTheOriginal()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        var day = new DateOnly(2026, 10, 14);
        fixture.Meetings.Add(new Meeting(day, new TimeOnly(9, 30), 45, "Standup"));

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F2));
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Home));
        for (int i = 0; i < 40; i++)
        {
            await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace));
        }

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        Assert.IsNotNull(fixture.Dialog.EntryErrorForTest);
        Assert.AreEqual("Standup", fixture.Meetings.On(day).Single().Title);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        Assert.IsNull(fixture.Dialog.EntryForTest);
        Assert.AreEqual("Standup", fixture.Meetings.On(day).Single().Title);
        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        AssertScreenContains(Render(fixture.Dialog), "New meeting:");
    }

    [TestMethod]
    public async Task EWithNoMeetingDoesNothing()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Char('e'));

        Assert.IsNull(fixture.Dialog.EntryForTest);
    }

    // The meetings list starts 15 rows into the dialog (border + 14).
    private static MouseEvent AtMeeting(CalendarDialog dialog, int row, int wheel = 0, bool click = true) => new()
    {
        Position = new iConsole.Data.Position(dialog.AbsolutePosition.X + 6, dialog.AbsolutePosition.Y + 15 + row),
        LeftClicked = click && wheel == 0,
        WheelDelta = wheel,
    };

    private static void AddThree(Fixture fixture)
    {
        var day = new DateOnly(2026, 10, 14);
        fixture.Meetings.Add(new Meeting(day, new TimeOnly(9, 0), 30, "One"));
        fixture.Meetings.Add(new Meeting(day, new TimeOnly(10, 0), 30, "Two"));
        fixture.Meetings.Add(new Meeting(day, new TimeOnly(11, 0), 30, "Three"));
    }

    [TestMethod]
    public async Task AClickOnAMeetingHighlightsIt()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        AddThree(fixture);

        Assert.IsTrue(await fixture.Dialog.HandleMouseEventAsync(AtMeeting(fixture.Dialog, 2)));

        Assert.AreEqual(2, fixture.Dialog.MeetingCursorForTest);
        await fixture.Dialog.HandleMouseEventAsync(AtMeeting(fixture.Dialog, 3));
        Assert.AreEqual(2, fixture.Dialog.MeetingCursorForTest, "a click below the last meeting changes nothing");
    }

    [TestMethod]
    public async Task TheWheelOverTheMeetingsMovesTheHighlight_AndStopsAtTheEnds()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        AddThree(fixture);

        await fixture.Dialog.HandleMouseEventAsync(AtMeeting(fixture.Dialog, 0, wheel: -1));
        await fixture.Dialog.HandleMouseEventAsync(AtMeeting(fixture.Dialog, 0, wheel: -1));
        await fixture.Dialog.HandleMouseEventAsync(AtMeeting(fixture.Dialog, 0, wheel: -1));
        Assert.AreEqual(2, fixture.Dialog.MeetingCursorForTest);

        await fixture.Dialog.HandleMouseEventAsync(AtMeeting(fixture.Dialog, 0, wheel: 1));
        Assert.AreEqual(1, fixture.Dialog.MeetingCursorForTest);
    }

    [TestMethod]
    public async Task TheMouseLeavesTheMeetingsAloneWhileTypingOrAsking()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        AddThree(fixture);

        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        await fixture.Dialog.HandleMouseEventAsync(AtMeeting(fixture.Dialog, 2));
        Assert.AreEqual(0, fixture.Dialog.MeetingCursorForTest);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        await fixture.Dialog.HandleKeyEventAsync(Char('d'));
        await fixture.Dialog.HandleMouseEventAsync(AtMeeting(fixture.Dialog, 2));
        Assert.AreEqual(0, fixture.Dialog.MeetingCursorForTest);
    }

    [TestMethod]
    public async Task TheMonthAndTheSelectedDayAreShownOnScreen()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);

        var screen = Render(fixture.Dialog);

        AssertScreenContains(screen, new DateOnly(2026, 10, 14).ToString("MMMM yyyy"));
        AssertScreenContains(screen, "today");
        AssertScreenContains(screen, "No meetings on this day");
    }

    private static iConsole.Data.MouseEvent ClickOn(CalendarDialog dialog, int column, int row) => new()
    {
        Position = new iConsole.Data.Position(dialog.GridForTest.AbsolutePosition.X + (uint)column, dialog.GridForTest.AbsolutePosition.Y + (uint)row),
        LeftClicked = true,
    };

    [TestMethod]
    public async Task TheMonthGridIsTheCalendarControlBoundToTheModel()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);

        Assert.AreSame(fixture.Dialog.CalendarForTest, fixture.Dialog.GridForTest.Model);
        Assert.AreEqual(fixture.Dialog.CalendarForTest.SelectedDate, fixture.Dialog.GridForTest.SelectedDate);
    }

    [TestMethod]
    public async Task AKeyMovesTheGridSelectionToo()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        Assert.AreEqual(new DateOnly(2026, 10, 21), fixture.Dialog.GridForTest.SelectedDate);
    }

    [TestMethod]
    public async Task AClickOnADaySelectsItAndShowsItsMeetings()
    {
        var fixture = Create();
        fixture.Meetings.Add(Meeting.TryParse("09:00 30 Review", new DateOnly(2026, 10, 20), out var meeting, out _) ? meeting! : throw new InvalidOperationException());
        await ShowAsync(fixture.Dialog);

        // 20 October 2026 is a Tuesday in the fourth grid row; the week-number column takes three cells.
        await fixture.Dialog.HandleMouseEventAsync(ClickOn(fixture.Dialog, 3 + 1 * 3, 2 + 3));

        Assert.AreEqual(new DateOnly(2026, 10, 20), fixture.Dialog.CalendarForTest.SelectedDate);
        AssertScreenContains(Render(fixture.Dialog), "Review");
    }

    [TestMethod]
    public async Task TheGridShowsWeekNumbersAndLeavesNeighbouringDaysBlank()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);

        var screen = Render(fixture.Dialog);

        AssertScreenContains(screen, "Wk");
        AssertScreenContains(screen, "42");
    }

    [TestMethod]
    public async Task CtrlVPastesTheMeetingLine_AndCtrlUClearsIt()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await iConsole.Services.TextClipboard.Current.SetTextAsync("14:30 45" + (char)10 + "Standup");
        await fixture.Dialog.HandleKeyEventAsync(Char('n'));

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.V, 'v', control: true));
        Assert.AreEqual("14:30 45 Standup", fixture.Dialog.EntryForTest);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, (char)13));
        Assert.AreEqual("Standup", fixture.Meetings.On(new DateOnly(2026, 10, 14)).Single().Title);

        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(fixture.Dialog, "oops");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.U, 'u', control: true));
        Assert.AreEqual(string.Empty, fixture.Dialog.EntryForTest);
    }
}
