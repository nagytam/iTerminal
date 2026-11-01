using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs.UnitConverter;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The unit converter follows the mouse as it follows the keys: picking a unit or a category recomputes at once.</summary>
[TestClass]
public class UnitConverterMouseTest
{
    private static async Task<UnitConverterDialog> OpenAsync()
    {
        UnitConverterDialog dialog = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        await ShowAsync(dialog);
        return dialog;
    }

    private static MouseEvent Click(Position origin, int column, int row) => new()
    {
        Position = new Position(origin.X + column, origin.Y + row),
        LeftClicked = true,
    };

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickOnAnotherToUnit_ChangesTheResultWithoutAKeyPress()
    {
        UnitConverterDialog dialog = await OpenAsync();
        int before = dialog.EngineForTest.ToUnitIndex;
        string resultBefore = dialog.ResultLine();
        int target = before == 0 ? 1 : 0;

        bool handled = await dialog.HandleMouseEventAsync(Click(dialog.ToUnitListBoxForTest.AbsolutePosition, 3, target));

        Assert.IsTrue(handled);
        Assert.AreEqual(target, dialog.EngineForTest.ToUnitIndex);
        Assert.AreNotEqual(resultBefore, dialog.ResultLine());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickOnAnotherFromUnit_IsTakenUp()
    {
        UnitConverterDialog dialog = await OpenAsync();
        int target = dialog.EngineForTest.FromUnitIndex == 2 ? 3 : 2;

        await dialog.HandleMouseEventAsync(Click(dialog.FromUnitListBoxForTest.AbsolutePosition, 3, target));

        Assert.AreEqual(target, dialog.EngineForTest.FromUnitIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickOnAnotherCategory_ChangesTheUnitsOnOffer()
    {
        UnitConverterDialog dialog = await OpenAsync();
        var before = dialog.EngineForTest.Category;

        await dialog.HandleMouseEventAsync(Click(dialog.CategoryListBoxForTest.AbsolutePosition, 3, 1));

        Assert.AreNotEqual(before, dialog.EngineForTest.Category);
        Assert.AreEqual(0, dialog.EngineForTest.FromUnitIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickOutsideTheDialog_ChangesNothing()
    {
        UnitConverterDialog dialog = await OpenAsync();
        int from = dialog.EngineForTest.FromUnitIndex;
        int to = dialog.EngineForTest.ToUnitIndex;

        await dialog.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), LeftClicked = true });

        Assert.AreEqual(from, dialog.EngineForTest.FromUnitIndex);
        Assert.AreEqual(to, dialog.EngineForTest.ToUnitIndex);
    }
}
