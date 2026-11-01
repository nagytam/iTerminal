using System.Globalization;
using iTerminal.Core.Model.Calendar;

namespace iTerminal.Core.Test.Model.Calendar;

[TestClass]
public class CalendarModelTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void OctoberGridStartsOnThursdayAndHoldsEveryDay()
    {
        // 1 October 2026 is a Thursday, so Monday to Wednesday of the first week are blank.
        var weeks = CalendarModel.BuildGrid(2026, 10);

        Assert.AreEqual(5, weeks.Count);
        Assert.IsNull(weeks[0].Days[0]);
        Assert.IsNull(weeks[0].Days[2]);
        Assert.AreEqual(new DateOnly(2026, 10, 1), weeks[0].Days[3]);
        Assert.AreEqual(31, weeks.SelectMany(w => w.Days).Count(d => d.HasValue));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryRowHasSevenColumns()
    {
        foreach (var week in CalendarModel.BuildGrid(2026, 2))
        {
            Assert.AreEqual(7, week.Days.Length);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WeekNumbersAreIsoWeeks()
    {
        // 29 December 2025 is a Monday that belongs to week 1 of 2026, so December 2025's last row is week 1.
        var december = CalendarModel.BuildGrid(2025, 12);

        Assert.AreEqual(1, december[^1].WeekNumber);
        Assert.AreEqual(ISOWeek.GetWeekOfYear(new DateTime(2025, 12, 1)), december[0].WeekNumber);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsoWeekMatchesTheStandardCalendar()
    {
        Assert.AreEqual(1, CalendarModel.IsoWeek(new DateOnly(2026, 1, 1)));
        Assert.AreEqual(ISOWeek.GetWeekOfYear(new DateTime(2026, 10, 14)), CalendarModel.IsoWeek(new DateOnly(2026, 10, 14)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MovingByMonthKeepsTheDayWhereTheMonthHasIt()
    {
        var model = new CalendarModel(new DateOnly(2026, 1, 31));

        model.MoveMonths(1);

        Assert.AreEqual(new DateOnly(2026, 2, 28), model.SelectedDate);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MovingBackAcrossNewYearChangesTheYear()
    {
        var model = new CalendarModel(new DateOnly(2026, 1, 15));

        model.MoveMonths(-1);

        Assert.AreEqual(new DateOnly(2025, 12, 15), model.SelectedDate);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MovingByYearFromLeapDayLandsOnTheLastDayOfFebruary()
    {
        var model = new CalendarModel(new DateOnly(2028, 2, 29));

        model.MoveYears(1);

        Assert.AreEqual(new DateOnly(2029, 2, 28), model.SelectedDate);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MovingPastTheLastDayStopsAtTheLastDay()
    {
        var model = new CalendarModel(DateOnly.MaxValue);

        model.MoveDays(1);
        model.MoveMonths(12);

        Assert.AreEqual(DateOnly.MaxValue, model.SelectedDate);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MovingByWeekAndDayAndTodayWork()
    {
        var model = new CalendarModel(new DateOnly(2026, 10, 14));

        model.MoveDays(7);
        Assert.AreEqual(new DateOnly(2026, 10, 21), model.SelectedDate);

        model.MoveDays(-1);
        Assert.AreEqual(new DateOnly(2026, 10, 20), model.SelectedDate);

        model.GoToToday();
        Assert.AreEqual(new DateOnly(2026, 10, 14), model.SelectedDate);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ResetStartsAgainFromTheGivenDay()
    {
        var model = new CalendarModel(new DateOnly(2026, 10, 14));
        model.MoveMonths(3);

        model.Reset(new DateOnly(2027, 1, 2));

        Assert.AreEqual(new DateOnly(2027, 1, 2), model.Today);
        Assert.AreEqual(new DateOnly(2027, 1, 2), model.SelectedDate);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(0, "today")]
    [DataRow(1, "tomorrow")]
    [DataRow(-1, "yesterday")]
    [DataRow(3, "in 3 days")]
    [DataRow(-4, "4 days ago")]
    public void RelativeTextDescribesTheDistanceFromToday(int days, string expected)
    {
        var today = new DateOnly(2026, 10, 14);

        Assert.AreEqual(expected, CalendarModel.RelativeTo(today.AddDays(days), today));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryChangeOfTheSelectionRaisesChanged()
    {
        var model = new CalendarModel(new DateOnly(2026, 10, 14));
        var raised = 0;
        model.Changed += (_, _) => raised++;

        model.MoveDays(1);
        model.MoveMonths(1);
        model.Select(new DateOnly(2027, 3, 3));
        model.GoToToday();

        Assert.AreEqual(4, raised);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SelectingTheSameDayIsNotAChange()
    {
        var model = new CalendarModel(new DateOnly(2026, 10, 14));
        var raised = 0;
        model.Changed += (_, _) => raised++;

        model.Select(new DateOnly(2026, 10, 14));
        model.MoveDays(0);

        Assert.AreEqual(0, raised);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ResetToANewTodayIsAChangeEvenWhenTheSelectionStays()
    {
        var model = new CalendarModel(new DateOnly(2026, 10, 14));
        var raised = 0;
        model.Changed += (_, _) => raised++;

        model.Reset(new DateOnly(2026, 10, 15));

        Assert.AreEqual(1, raised);
        Assert.AreEqual(new DateOnly(2026, 10, 15), model.SelectedDate);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheModelIsACalendarControlModel()
    {
        iConsole.Model.ICalendarModel model = new CalendarModel(new DateOnly(2026, 10, 14));
        model.Select(new DateOnly(2026, 10, 20));

        Assert.AreEqual(new DateOnly(2026, 10, 20), model.SelectedDate);
        Assert.AreEqual(new DateOnly(2026, 10, 14), model.Today);
    }
}
