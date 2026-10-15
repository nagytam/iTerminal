using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Tabs;

[TestClass]
public class TabTest
{
    private Mock<IThemeService> CreateMockThemeService(ColorScheme colorScheme = null)
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme ?? new ColorScheme());
        return mockThemeService;
    }

    #region Creation and Initialization Tests

    [TestMethod]
    public void TabCanBeCreated()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);

        Assert.IsNotNull(tab);
        Assert.IsInstanceOfType(tab, typeof(Control));
    }

    [TestMethod]
    public void TabHasEmptyTabPagesOnCreation()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);

        Assert.AreEqual(0, tab.TabPages.Count);
    }

    [TestMethod]
    public void TabHasDefaultHorizontalOrientation()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);

        Assert.AreEqual(Orientation.Horizontal, tab.Orientation);
    }

    [TestMethod]
    public void TabInitialFocusedPageIsZero()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);

        Assert.AreEqual(0, tab.FocusedTabPage);
    }

    [TestMethod]
    public async Task TabInitializeAsyncInitializesAllPages()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object) { ViewSize = new Size(30, 10) };

        var tabPage1 = new TabPage("Page1", mockThemeService.Object);
        var tabPage2 = new TabPage("Page2", mockThemeService.Object);
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);

        await tab.OnInitializeAsync();

        Assert.AreEqual(2, tab.TabPages.Count);
    }

    #endregion

    #region Tab Page Management Tests

    [TestMethod]
    public void AddTabPageAddsPageToCollection()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);
        var tabPage = new TabPage("Page1", mockThemeService.Object);

        tab.AddTabPage(tabPage);

        Assert.AreEqual(1, tab.TabPages.Count);
        Assert.AreEqual(tabPage, tab.TabPages[0]);
    }

    [TestMethod]
    public void AddTabPageSetsFocusOnFirstPage()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);
        var tabPage = new TabPage("Page1", mockThemeService.Object);

        tab.AddTabPage(tabPage);

        // FocusedTabPage should be 0 after adding first page
        Assert.AreEqual(0, tab.FocusedTabPage);
    }

    [TestMethod]
    public void AddTabPageSetsParent()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);
        var tabPage = new TabPage("Page1", mockThemeService.Object);

        tab.AddTabPage(tabPage);

        Assert.AreEqual(tab, tabPage.Parent);
    }

    [TestMethod]
    public void AddMultipleTabPagesDoesNotFocusSecondPage()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);
        var tabPage1 = new TabPage("Page1", mockThemeService.Object);
        var tabPage2 = new TabPage("Page2", mockThemeService.Object);

        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);

        Assert.AreEqual(0, tab.FocusedTabPage);
    }

    #endregion

    #region Focus Management Tests

    [TestMethod]
    public void FocusedTabPagePropertyGetReturnsCurrentFocus()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);
        var tabPage = new TabPage("Page1", mockThemeService.Object);
        tab.AddTabPage(tabPage);

        Assert.AreEqual(0, tab.FocusedTabPage);
    }

    [TestMethod]
    public void FocusedTabPagePropertySetUpdatesFocus()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);
        var tabPage1 = new TabPage("Page1", mockThemeService.Object);
        var tabPage2 = new TabPage("Page2", mockThemeService.Object);
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);

        tab.FocusedTabPage = 1;

        Assert.AreEqual(1, tab.FocusedTabPage);
    }

    [TestMethod]
    public void FocusedTabPagePropertyClampsBelowZero()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);
        var tabPage = new TabPage("Page1", mockThemeService.Object);
        tab.AddTabPage(tabPage);

        tab.FocusedTabPage = -5;

        Assert.AreEqual(0, tab.FocusedTabPage);
    }

    [TestMethod]
    public void FocusedTabPagePropertyClampsAboveMax()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);
        var tabPage1 = new TabPage("Page1", mockThemeService.Object);
        var tabPage2 = new TabPage("Page2", mockThemeService.Object);
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);

        tab.FocusedTabPage = 10;

        Assert.AreEqual(1, tab.FocusedTabPage);
    }

    [TestMethod]
    public void FocusedTabPagePropertyReturnsZeroWhenEmpty()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);

        tab.FocusedTabPage = 5;

        Assert.AreEqual(0, tab.FocusedTabPage);
    }

    #endregion

    #region Controls and Commands Delegation Tests

    [TestMethod]
    public void ControlsPropertyDelegatestoFocusedPage()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);
        var tabPage = new TabPage("Page1", mockThemeService.Object);
        var label = new Label("Test", mockThemeService.Object);
        tabPage.AddControl(label);
        tab.AddTabPage(tabPage);

        var controls = tab.Controls;

        Assert.AreEqual(1, controls.Count);
        Assert.AreEqual(label, controls[0]);
    }

    [TestMethod]
    public void CommandsPropertyDelegatestoFocusedPage()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);
        var tabPage = new TabPage("Page1", mockThemeService.Object);
        tab.AddTabPage(tabPage);

        var commands = tab.Commands;

        Assert.IsNotNull(commands);
    }

    #endregion

    #region Layout Tests

    [TestMethod]
    public async Task TabControlHorizontalLayout()
    {
        var mockThemeService = CreateMockThemeService();
        var buffer = new MockBuffer(new Size(30, 13));
        var tabPage1 = new TabPage("Page1", mockThemeService.Object);
        tabPage1.AddControl(new Label("Label1", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        });
        var tabPage2 = new TabPage("Page2", mockThemeService.Object);
        tabPage2.AddControl(new Label("Label2 Label2", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        });
        var tab = new Tab(mockThemeService.Object) { ViewSize = buffer.Size };
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);
        await tab.OnInitializeAsync();

        Assert.AreEqual(0, (int)tabPage1.Position.X);
        Assert.AreEqual(3, (int)tabPage1.Position.Y);
        Assert.AreEqual(30, (int)tabPage1.ViewSize.Width);
        Assert.AreEqual(10, (int)tabPage1.ViewSize.Height);
        Assert.AreEqual(0, (int)tabPage2.Position.X);
        Assert.AreEqual(3, (int)tabPage2.Position.Y);
        Assert.AreEqual(30, (int)tabPage2.ViewSize.Width);
        Assert.AreEqual(10, (int)tabPage2.ViewSize.Height);
    }

    [TestMethod]
    public async Task TabControlVerticalLayout()
    {
        var mockThemeService = CreateMockThemeService();
        var buffer = new MockBuffer(new Size(30, 13));
        var tabPage1 = new TabPage("Page1", mockThemeService.Object);
        var tabPage2 = new TabPage("Page2", mockThemeService.Object);
        var tab = new Tab(mockThemeService.Object)
        {
            Orientation = Orientation.Vertical,
            ViewSize = buffer.Size
        };
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);
        await tab.OnInitializeAsync();

        Assert.AreEqual(11, (int)tabPage1.Position.X);
        Assert.AreEqual(0, (int)tabPage1.Position.Y);
        Assert.AreEqual(19, (int)tabPage1.ViewSize.Width);
        Assert.AreEqual(13, (int)tabPage1.ViewSize.Height);
    }

    [TestMethod]
    public async Task TabControlHorizontal()
    {
        var mockThemeService = CreateMockThemeService();
        var buffer = new MockBuffer(new Size(30, 13));
        var tabPage1 = new TabPage("Page1", mockThemeService.Object);
        tabPage1.AddControl(new Label("Label1", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        });
        var tabPage2 = new TabPage("Page2", mockThemeService.Object);
        tabPage2.AddControl(new Label("Label2 Label2", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        });
        var tab = new Tab(mockThemeService.Object) { ViewSize = buffer.Size };
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);
        await tab.OnInitializeAsync();
        Assert.IsNotNull(tab);
        Assert.AreEqual(0, (int)tab.Position.X);
        Assert.AreEqual(0, (int)tab.Position.Y);
        Assert.AreEqual(30, (int)tab.ViewSize.Width);
        Assert.AreEqual(13, (int)tab.ViewSize.Height);
        Assert.AreEqual(0, (int)tabPage1.Position.X);
        Assert.AreEqual(3, (int)tabPage1.Position.Y);
        Assert.AreEqual(30, (int)tabPage1.ViewSize.Width);
        Assert.AreEqual(10, (int)tabPage1.ViewSize.Height);
        Assert.AreEqual(0, (int)tabPage2.Position.X);
        Assert.AreEqual(3, (int)tabPage2.Position.Y);
        Assert.AreEqual(30, (int)tabPage2.ViewSize.Width);
        Assert.AreEqual(10, (int)tabPage2.ViewSize.Height);

        tab.Draw(buffer);
        var contentLines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual("╔════════════════════════════╗", contentLines[0]);
        Assert.AreEqual("║< Page1 >│ Page2 │          ║", contentLines[1]);
        Assert.AreEqual("╚════════════════════════════╝", contentLines[2]);
        Assert.AreEqual("Label1", contentLines[3].TrimEnd());
    }

    [TestMethod]
    public async Task TabControlVertical()
    {
        var mockThemeService = CreateMockThemeService();
        var buffer = new MockBuffer(new Size(30, 13));
        var tabPage1 = new TabPage("Page1", mockThemeService.Object);
        tabPage1.AddControl(new Label("Label1", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        });
        var tabPage2 = new TabPage("Page2", mockThemeService.Object);
        tabPage2.AddControl(new Label("Label2 Label2", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        });
        var tab = new Tab(mockThemeService.Object) 
        { 
            Orientation = Orientation.Vertical,
            ViewSize = buffer.Size 
        };
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);
        await tab.OnInitializeAsync();
        Assert.IsNotNull(tab);
        Assert.AreEqual(0, (int)tab.Position.X);
        Assert.AreEqual(0, (int)tab.Position.Y);
        Assert.AreEqual(30, (int)tab.ViewSize.Width);
        Assert.AreEqual(13, (int)tab.ViewSize.Height);
        Assert.AreEqual(11, (int)tabPage1.Position.X);
        Assert.AreEqual(0, (int)tabPage1.Position.Y);
        Assert.AreEqual(19, (int)tabPage1.ViewSize.Width);
        Assert.AreEqual(13, (int)tabPage1.ViewSize.Height);
        Assert.AreEqual(11, (int)tabPage2.Position.X);
        Assert.AreEqual(0, (int)tabPage2.Position.Y);
        Assert.AreEqual(19, (int)tabPage2.ViewSize.Width);
        Assert.AreEqual(13, (int)tabPage2.ViewSize.Height);

        tab.Draw(buffer);
        var contentLines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual("╔═════════╗Label1", contentLines[0].TrimEnd());
        Assert.AreEqual("║< Page1 >║", contentLines[1]);
        Assert.AreEqual("╠═════════╣", contentLines[2]);
        Assert.AreEqual("║  Page2  ║", contentLines[3]);
        Assert.AreEqual("╚═════════╝", contentLines[4]);
    }

    #endregion

    #region Drawing Tests

    [TestMethod]
    public void DrawWithEmptyTabPagesDoesNotThrow()
    {
        var mockThemeService = CreateMockThemeService();
        var buffer = new MockBuffer(new Size(30, 10));
        var tab = new Tab(mockThemeService.Object) { ViewSize = buffer.Size };

        tab.Draw(buffer);

        Assert.IsNotNull(buffer);
    }

    [TestMethod]
    public async Task DrawHorizontalRendersTabStrip()
    {
        var mockThemeService = CreateMockThemeService();
        var buffer = new MockBuffer(new Size(30, 13));
        var tabPage1 = new TabPage("Page1", mockThemeService.Object);
        var tabPage2 = new TabPage("Page2", mockThemeService.Object);
        var tab = new Tab(mockThemeService.Object) { ViewSize = buffer.Size };
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);
        await tab.OnInitializeAsync();

        tab.Draw(buffer);
        var contentLines = buffer.ContentAsString.Split(Environment.NewLine);

        Assert.IsTrue(contentLines.Length > 0);
        Assert.IsTrue(contentLines[0].Contains("╔"));
        Assert.IsTrue(contentLines[1].Contains("Page1"));
    }

    [TestMethod]
    public async Task DrawVerticalRendersTabStrip()
    {
        var mockThemeService = CreateMockThemeService();
        var buffer = new MockBuffer(new Size(30, 13));
        var tabPage1 = new TabPage("Page1", mockThemeService.Object);
        var tabPage2 = new TabPage("Page2", mockThemeService.Object);
        var tab = new Tab(mockThemeService.Object)
        {
            Orientation = Orientation.Vertical,
            ViewSize = buffer.Size
        };
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);
        await tab.OnInitializeAsync();

        tab.Draw(buffer);
        var contentLines = buffer.ContentAsString.Split(Environment.NewLine);

        Assert.IsTrue(contentLines.Length > 0);
        Assert.IsTrue(contentLines[0].Contains("╔"));
        Assert.IsTrue(contentLines[1].Contains("Page1"));
    }

    [TestMethod]
    public async Task DrawShowsFocusedPageContent()
    {
        var mockThemeService = CreateMockThemeService();
        var buffer = new MockBuffer(new Size(30, 13));
        var tabPage1 = new TabPage("Page1", mockThemeService.Object);
        tabPage1.AddControl(new Label("Content1", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        });
        var tabPage2 = new TabPage("Page2", mockThemeService.Object);
        tabPage2.AddControl(new Label("Content2", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        });
        var tab = new Tab(mockThemeService.Object) { ViewSize = buffer.Size };
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);
        await tab.OnInitializeAsync();

        tab.Draw(buffer);
        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("Content1"));
        Assert.IsFalse(content.Contains("Content2"));
    }

    #endregion

    #region Hot Key Tests

    [TestMethod]
    public async Task TabControlSwitchByHotKey()
    {
        var mockThemeService = CreateMockThemeService();
        var buffer = new MockBuffer(new Size(30, 10));

        var tabPage1 = new TabPage("Page1", mockThemeService.Object)
        {
            HotKeyChar = '1',
            HotConsoleKey = ConsoleKey.D1
        };
        tabPage1.AddControl(new Label("Label1", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        });

        var tabPage2 = new TabPage("Page2", mockThemeService.Object)
        {
            HotKeyChar = '2',
            HotConsoleKey = ConsoleKey.D2
        };
        tabPage2.AddControl(new Label("Label2", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        });

        var tab = new Tab(mockThemeService.Object) { ViewSize = buffer.Size };
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);
        await tab.OnInitializeAsync();

        tab.Draw(buffer);
        var before = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual("Label1", before[3].TrimEnd());
        Assert.AreEqual(0, tab.FocusedTabPage);

        await tabPage2.TriggerHotKeyAsync();
        tab.FocusedTabPage = 1;

        buffer.Clear();
        tab.Draw(buffer);
        var after = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual("Label2", after[3].TrimEnd());
    }

    [TestMethod]
    public async Task HandleHotKeyEventAsyncSwitchesTabsWithControl()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object) { ViewSize = new Size(30, 10) };
        var tabPage1 = new TabPage("Page1", mockThemeService.Object) { HotKeyChar = '1', HotConsoleKey = ConsoleKey.D1 };
        var tabPage2 = new TabPage("Page2", mockThemeService.Object) { HotKeyChar = '2', HotConsoleKey = ConsoleKey.D2 };
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);
        await tab.OnInitializeAsync();

        var keyEvent = new KeyEvent('2', ConsoleKey.D2, shift: false, alt: false, control: true);
        var handled = await tab.HandleHotKeyEventAsync(keyEvent);

        Assert.IsTrue(handled);
        Assert.AreEqual(1, tab.FocusedTabPage);
    }

    [TestMethod]
    public async Task HandleHotKeyEventAsync_PutsTheCursorOnTheArrivingPagesFirstControl()
    {
        // A TabPage cannot hold focus itself, so focusing the page hands the cursor to the first
        // focusable control inside it. That is what makes the arrow keys work the moment a page comes
        // up, and why a page's list belongs in front of its buttons.
        var mockThemeService = CreateMockThemeService();
        var (tab, firstList) = CreateTabWithLists(Orientation.Vertical, mockThemeService.Object);
        tab.TabPages[0].HotConsoleKey = ConsoleKey.D0;
        tab.TabPages[1].HotConsoleKey = ConsoleKey.D1;
        await tab.OnInitializeAsync();
        SettleList(firstList);

        var handled = await tab.HandleHotKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.D1, shift: false, alt: false, control: true));

        Assert.IsTrue(handled);
        Assert.AreEqual(1, tab.FocusedTabPage);
        Assert.AreSame(
            tab.TabPages[1].Controls[0],
            tab.TabPages[1].GetFocusedControl(),
            "The page's first control should hold the cursor.");
    }

    [TestMethod]
    public async Task HandleHotKeyEventAsync_OnThePageAlreadyShowing_BringsTheCursorBackToItsFirstControl()
    {
        // The keyboard's counterpart to clicking the name of the page already up. Read as "only when
        // that page is not already focused", the key did nothing once the cursor had moved onto one of
        // the page's buttons — and nothing else brought it back to the list.
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object)
        {
            Orientation = Orientation.Vertical,
            ViewSize = new Size(40, 15)
        };
        var page = new TabPage("Page0", mockThemeService.Object) { HotConsoleKey = ConsoleKey.D0 };
        var items = new ConcurrentObservableCollection<string>();
        items.Add("row0");
        var listBox = new ListBox<string>(new ListBoxModel<string>(items), mockThemeService.Object)
        {
            ViewSize = new Size(20, 1)
        };
        // Stands in for the page's Select button: a second focusable control, added after the list.
        var laterControl = new CheckBox(mockThemeService.Object) { Label = "Select" };
        page.AddControl(listBox);
        page.AddControl(laterControl);
        tab.AddTabPage(page);
        tab.AddTabPage(new TabPage("Page1", mockThemeService.Object) { HotConsoleKey = ConsoleKey.D1 });
        await tab.OnInitializeAsync();
        SettleList(listBox);
        laterControl.SetFocus();
        Assert.IsTrue(laterControl.IsFocused, "The cursor has to be off the list for the test to mean anything.");

        var handled = await tab.HandleHotKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.D0, shift: false, alt: false, control: true));

        Assert.IsTrue(handled);
        Assert.IsTrue(listBox.IsFocused, "Ctrl+<number> is the way back into the page's content.");
        Assert.IsFalse(laterControl.IsFocused);
    }

    [TestMethod]
    public async Task SwitchingPage_TakesAnOpenOverlayWithIt()
    {
        // A suggestion list is a Popup, and a Popup attaches itself to the *root* rather than to the
        // control that opened it — so leaving the page does not hide it, and it would be left floating
        // over whichever page arrives in its place.
        var mockThemeService = CreateMockThemeService();
        var (tab, firstList) = CreateTabWithLists(Orientation.Vertical, mockThemeService.Object);
        var host = new OverlayHostControl(mockThemeService.Object) { IsOverlayOpen = true };
        tab.TabPages[0].AddControl(host);
        await tab.OnInitializeAsync();
        SettleList(firstList);

        await tab.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: true));

        Assert.AreEqual(1, tab.FocusedTabPage);
        Assert.IsFalse(host.IsOverlayOpen, "The list belonged to the page that was left behind.");
    }

    /// <summary>
    /// Stands in for a control with something floating over the page — an autocompletion's suggestion
    /// list is the real case, and it is deliberately not used here: what matters is that the Tab
    /// honours the contract, not which control implements it.
    /// </summary>
    private sealed class OverlayHostControl(IThemeService themeService)
        : Control(themeService), ITransientOverlayHost
    {
        public bool IsOverlayOpen { get; set; }

        public void CloseOverlay() => IsOverlayOpen = false;
    }

    [TestMethod]
    public async Task HandleHotKeyEventAsyncIgnoresWithoutControl()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object) { ViewSize = new Size(30, 10) };
        var tabPage1 = new TabPage("Page1", mockThemeService.Object) { HotKeyChar = '1', HotConsoleKey = ConsoleKey.D1 };
        var tabPage2 = new TabPage("Page2", mockThemeService.Object) { HotKeyChar = '2', HotConsoleKey = ConsoleKey.D2 };
        tab.AddTabPage(tabPage1);
        tab.AddTabPage(tabPage2);
        await tab.OnInitializeAsync();

        var keyEvent = new KeyEvent('2', ConsoleKey.D2, shift: false, alt: false, control: false);
        var handled = await tab.HandleHotKeyEventAsync(keyEvent);

        Assert.AreEqual(0, tab.FocusedTabPage);
    }

    #endregion

    #region Arrow Key Routing Tests

    /// <summary>
    /// A tab page carrying one focusable list, sized so the list has rows to move through.
    /// </summary>
    private static (Tab tab, ListBox<string> firstList) CreateTabWithLists(
        Orientation orientation,
        IThemeService themeService,
        int rowCount = 5)
    {
        var tab = new Tab(themeService)
        {
            Orientation = orientation,
            ViewSize = new Size(40, 15)
        };

        ListBox<string>? firstList = null;
        for (int page = 0; page < 2; page++)
        {
            var items = new ConcurrentObservableCollection<string>();
            for (int row = 0; row < rowCount; row++)
            {
                items.Add($"page{page}-row{row}");
            }
            var listBox = new ListBox<string>(new ListBoxModel<string>(items), themeService)
            {
                Position = new Position(0, 0),
                ViewSize = new Size(20, rowCount)
            };
            var tabPage = new TabPage($"Page{page}", themeService);
            tabPage.AddControl(listBox);
            tab.AddTabPage(tabPage);
            firstList ??= listBox;
        }
        return (tab, firstList!);
    }

    /// <summary>
    /// The list's rows only exist once the model has been projected, which the ListBox does on idle.
    /// Without this the scrollable height is zero and every scroll command is a no-op.
    /// </summary>
    private static void SettleList(ListBox<string> listBox)
    {
        listBox.UpdateModel();
    }

    /// <summary>
    /// A page that claims a key ahead of its own children — the only reason to override
    /// <see cref="Control.HandleKeyEventAsync"/> on a page at all.
    /// </summary>
    private sealed class ClaimingTabPage : TabPage
    {
        public ClaimingTabPage(string name, IThemeService themeService) : base(name, themeService)
        {
        }

        public int ClaimCount { get; private set; }

        public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
        {
            if (keyEvent.ConsoleKey == ConsoleKey.Enter)
            {
                ClaimCount++;
                return true;
            }
            return await base.HandleKeyEventAsync(keyEvent);
        }
    }

    private static MouseEvent Click(int x, int y) =>
        new() { Position = new Position(x, y), LeftClicked = true };

    [TestMethod]
    public async Task HandleMouseEventAsync_VerticalStrip_ClickingAPageNameGoesThereAndFocusesItsContent()
    {
        // The strip is painted by the Tab, not built from child controls, so a click on a page name
        // has nothing under it to route to and used to do nothing at all.
        var mockThemeService = CreateMockThemeService();
        var (tab, firstList) = CreateTabWithLists(Orientation.Vertical, mockThemeService.Object);
        await tab.OnInitializeAsync();
        SettleList(firstList);

        // Names sit on every other row from the top border down: page 0 at y=1, page 1 at y=3.
        var handled = await tab.HandleMouseEventAsync(Click(3, 3));

        Assert.IsTrue(handled);
        Assert.AreEqual(1, tab.FocusedTabPage);
        Assert.IsInstanceOfType<ListBox<string>>(
            tab.TabPages[1].GetFocusedControl(),
            "The cursor belongs in the page's content, not on the strip that was clicked.");
    }

    [TestMethod]
    public async Task HandleMouseEventAsync_VerticalStrip_ClickingThePageAlreadyShowing_PutsFocusBackInItsContent()
    {
        var mockThemeService = CreateMockThemeService();
        var (tab, firstList) = CreateTabWithLists(Orientation.Vertical, mockThemeService.Object);
        await tab.OnInitializeAsync();
        SettleList(firstList);
        tab.ResetFocus();

        var handled = await tab.HandleMouseEventAsync(Click(3, 1));

        Assert.IsTrue(handled);
        Assert.AreEqual(0, tab.FocusedTabPage);
        Assert.IsTrue(firstList.IsFocused, "Clicking the current page is the way back into its content.");
    }

    [TestMethod]
    public async Task HandleMouseEventAsync_VerticalStrip_ClickingTheRuleBetweenTwoNamesChangesNothing()
    {
        var mockThemeService = CreateMockThemeService();
        var (tab, firstList) = CreateTabWithLists(Orientation.Vertical, mockThemeService.Object);
        await tab.OnInitializeAsync();
        SettleList(firstList);

        await tab.HandleMouseEventAsync(Click(3, 2));

        Assert.AreEqual(0, tab.FocusedTabPage, "The rules between the names stand for no page.");
    }

    [TestMethod]
    public async Task HandleMouseEventAsync_VerticalStrip_AClickPastTheStripIsNotATabClick()
    {
        // "Page0" plus its padding and borders is 11 columns; anything further right is the page.
        var mockThemeService = CreateMockThemeService();
        var (tab, firstList) = CreateTabWithLists(Orientation.Vertical, mockThemeService.Object);
        await tab.OnInitializeAsync();
        SettleList(firstList);

        await tab.HandleMouseEventAsync(Click(20, 3));

        Assert.AreEqual(0, tab.FocusedTabPage);
    }

    [TestMethod]
    public async Task HandleMouseEventAsync_HorizontalStrip_ClickingAPageNameGoesThere()
    {
        // One row of names under the top border: "Page0" spans x 1..9, "Page1" x 10..18.
        var mockThemeService = CreateMockThemeService();
        var (tab, firstList) = CreateTabWithLists(Orientation.Horizontal, mockThemeService.Object);
        await tab.OnInitializeAsync();
        SettleList(firstList);

        var handled = await tab.HandleMouseEventAsync(Click(12, 1));

        Assert.IsTrue(handled);
        Assert.AreEqual(1, tab.FocusedTabPage);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_GivesTheKeyToTheFocusedPage_NotStraightToItsChildren()
    {
        // Controls is overridden to return the focused page's children, so the base implementation
        // reaches past the page — and a page that overrides HandleKeyEventAsync was never called.
        var mockThemeService = CreateMockThemeService();
        var (tab, firstList) = CreateTabWithLists(Orientation.Vertical, mockThemeService.Object);
        var claimingPage = new ClaimingTabPage("3-Claiming", mockThemeService.Object);
        tab.AddTabPage(claimingPage);
        await tab.OnInitializeAsync();
        SettleList(firstList);
        tab.FocusedTabPage = tab.TabPages.Count - 1;
        claimingPage.SetFocus();

        var handled = await tab.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));

        Assert.IsTrue(handled);
        Assert.AreEqual(1, claimingPage.ClaimCount, "The focused page must be offered the key.");
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_AnUnfocusedPage_IsNotOfferedTheKeyTwice()
    {
        var mockThemeService = CreateMockThemeService();
        var mockTheme = mockThemeService.Object;
        var tab = new Tab(mockTheme) { Orientation = Orientation.Vertical, ViewSize = new Size(30, 10) };
        var claimingPage = new ClaimingTabPage("0-Claiming", mockTheme);
        tab.AddTabPage(claimingPage);
        tab.AddTabPage(new TabPage("1-Other", mockTheme));
        await tab.OnInitializeAsync();
        tab.ResetFocus();

        var handled = await tab.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));

        Assert.IsTrue(handled);
        Assert.AreEqual(1, claimingPage.ClaimCount, "The page on screen answers the key once, focused or not.");
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_VerticalTabWithFocusedList_ArrowMovesTheListNotThePage()
    {
        var mockThemeService = CreateMockThemeService();
        var (tab, firstList) = CreateTabWithLists(Orientation.Vertical, mockThemeService.Object);
        await tab.OnInitializeAsync();
        SettleList(firstList);

        var handled = await tab.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false));

        Assert.IsTrue(handled);
        Assert.AreEqual(0, tab.FocusedTabPage, "A plain arrow must not change page while a list has focus.");
        Assert.AreEqual(1, (int)firstList.ScrollableModel.CursorPosition.Y, "The focused list should have moved.");
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_VerticalTabWithFocusedList_CtrlArrowSwitchesPage()
    {
        var mockThemeService = CreateMockThemeService();
        var (tab, firstList) = CreateTabWithLists(Orientation.Vertical, mockThemeService.Object);
        await tab.OnInitializeAsync();
        SettleList(firstList);

        var handled = await tab.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: true));

        Assert.IsTrue(handled);
        Assert.AreEqual(1, tab.FocusedTabPage);
        Assert.AreEqual(0, (int)firstList.ScrollableModel.CursorPosition.Y, "Ctrl+arrow belongs to the tab strip alone.");
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_VerticalTabWithNothingUsingArrows_ArrowSwitchesPage()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object)
        {
            Orientation = Orientation.Vertical,
            ViewSize = new Size(30, 10)
        };
        tab.AddTabPage(new TabPage("Page1", mockThemeService.Object));
        tab.AddTabPage(new TabPage("Page2", mockThemeService.Object));
        await tab.OnInitializeAsync();

        var handled = await tab.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false));

        Assert.IsTrue(handled);
        Assert.AreEqual(1, tab.FocusedTabPage);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_HorizontalTabWithFocusedList_LeftRightReachTheTabStrip()
    {
        // A vertical list ignores Left/Right, so on a horizontal strip the arrow falls through to
        // the page switch — which is the behaviour the orientation implies.
        var mockThemeService = CreateMockThemeService();
        var (tab, firstList) = CreateTabWithLists(Orientation.Horizontal, mockThemeService.Object);
        await tab.OnInitializeAsync();
        SettleList(firstList);

        var handled = await tab.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));

        Assert.IsTrue(handled);
        Assert.AreEqual(1, tab.FocusedTabPage);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_VerticalTab_UpAtFirstPageStaysThere()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object)
        {
            Orientation = Orientation.Vertical,
            ViewSize = new Size(30, 10)
        };
        tab.AddTabPage(new TabPage("Page1", mockThemeService.Object));
        tab.AddTabPage(new TabPage("Page2", mockThemeService.Object));
        await tab.OnInitializeAsync();

        var handled = await tab.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.UpArrow, shift: false, alt: false, control: true));

        Assert.IsTrue(handled, "The key still belongs to the strip; it simply has nowhere to go.");
        Assert.AreEqual(0, tab.FocusedTabPage);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_SinglePage_LeavesArrowsToTheContent()
    {
        var mockThemeService = CreateMockThemeService();
        var items = new ConcurrentObservableCollection<string>();
        items.Add("row0");
        items.Add("row1");
        var listBox = new ListBox<string>(new ListBoxModel<string>(items), mockThemeService.Object)
        {
            ViewSize = new Size(20, 2)
        };
        var tabPage = new TabPage("Only", mockThemeService.Object);
        tabPage.AddControl(listBox);
        var tab = new Tab(mockThemeService.Object)
        {
            Orientation = Orientation.Vertical,
            ViewSize = new Size(30, 10)
        };
        tab.AddTabPage(tabPage);
        await tab.OnInitializeAsync();
        SettleList(listBox);

        await tab.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false));

        Assert.AreEqual(1, (int)listBox.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public void GetKeyHints_WithSeveralPages_NamesTheVerticalPageSwitchKeys()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object) { Orientation = Orientation.Vertical };
        tab.AddTabPage(new TabPage("Page1", mockThemeService.Object));
        tab.AddTabPage(new TabPage("Page2", mockThemeService.Object));

        var hints = tab.GetKeyHints().ToList();

        Assert.IsTrue(hints.Any(h => h.Key.Contains("Ctrl+Up")), "Ctrl+Up should be documented.");
        Assert.IsTrue(hints.Any(h => h.Key.Contains("Ctrl+<number>")), "The numbered jump should be documented.");
        Assert.IsFalse(hints.Any(h => h.Key.Contains("Left")), "A vertical strip should not advertise Left/Right.");
    }

    [TestMethod]
    public void GetKeyHints_WithOnePage_SaysNothing()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object) { Orientation = Orientation.Vertical };
        tab.AddTabPage(new TabPage("Only", mockThemeService.Object));

        Assert.AreEqual(0, tab.GetKeyHints().Count());
    }

    #endregion

    #region Show/Hide Tests

    [TestMethod]
    public async Task ShowAsyncShowsFocusedPage()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object) { ViewSize = new Size(30, 10) };
        var tabPage = new TabPage("Page1", mockThemeService.Object);
        tab.AddTabPage(tabPage);
        await tab.OnInitializeAsync();

        await tab.ShowAsync();

        Assert.IsTrue(tab.IsVisible);
    }

    [TestMethod]
    public async Task HideHidesFocusedPage()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object) { ViewSize = new Size(30, 10) };
        var tabPage = new TabPage("Page1", mockThemeService.Object);
        tab.AddTabPage(tabPage);
        await tab.OnInitializeAsync();

        tab.Hide();

        Assert.IsFalse(tab.IsVisible);
    }

    #endregion

    #region Edge Cases Tests

    [TestMethod]
    public async Task TabWithSinglePageInitializesCorrectly()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object) { ViewSize = new Size(30, 10) };
        var tabPage = new TabPage("OnlyPage", mockThemeService.Object);
        tab.AddTabPage(tabPage);

        await tab.OnInitializeAsync();

        Assert.AreEqual(1, tab.TabPages.Count);
        Assert.AreEqual(0, tab.FocusedTabPage);
    }

    [TestMethod]
    public async Task TabWithThreeOrMorePages()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object) { ViewSize = new Size(40, 15) };

        for (int i = 0; i < 5; i++)
        {
            tab.AddTabPage(new TabPage($"Page{i + 1}", mockThemeService.Object));
        }

        await tab.OnInitializeAsync();

        Assert.AreEqual(5, tab.TabPages.Count);
        Assert.AreEqual(0, tab.FocusedTabPage);
    }

    [TestMethod]
    public void TabPageNamesWithSpecialCharactersWork()
    {
        var mockThemeService = CreateMockThemeService();
        var tab = new Tab(mockThemeService.Object);
        var tabPage = new TabPage("Tab [1] #Active!", mockThemeService.Object);

        tab.AddTabPage(tabPage);

        Assert.AreEqual("Tab [1] #Active!", tab.TabPages[0].Name);
    }

    #endregion

    #region Close and Hide

    private sealed class RecordingControl(IThemeService themeService) : Control(themeService)
    {
        public int Closes { get; private set; }
        public int Hides { get; private set; }
        public override void Close() { Closes++; base.Close(); }
        public override void Hide() { Hides++; base.Hide(); }
    }

    /// <summary>
    /// Tab.Controls is the showing page's children, so closing a tab reached only that page. A page
    /// that was not showing kept whatever it owned: the folder selector's Path page left its
    /// suggestion list, attached to the root, over the panels after the selector had closed.
    /// </summary>
    [TestMethod]
    public void ClosingATabClosesEveryPageNotOnlyTheOneShowing()
    {
        var theme = CreateMockThemeService().Object;
        var tab = new Tab(theme);
        var showing = new TabPage("Showing", theme);
        var hidden = new TabPage("Hidden", theme);
        var onShowing = new RecordingControl(theme);
        var onHidden = new RecordingControl(theme);
        showing.AddControl(onShowing);
        hidden.AddControl(onHidden);
        tab.AddTabPage(showing);
        tab.AddTabPage(hidden);

        tab.Close();

        Assert.AreEqual(1, onShowing.Closes);
        Assert.AreEqual(1, onHidden.Closes, "the page that was not showing is closed too");
    }

    [TestMethod]
    public void HidingATabHidesEveryPage()
    {
        var theme = CreateMockThemeService().Object;
        var tab = new Tab(theme);
        var first = new TabPage("First", theme);
        var second = new TabPage("Second", theme);
        var onSecond = new RecordingControl(theme);
        second.AddControl(onSecond);
        tab.AddTabPage(first);
        tab.AddTabPage(second);

        tab.Hide();

        Assert.IsTrue(onSecond.Hides >= 1);
        Assert.IsFalse(second.IsVisible);
    }

    #endregion

    /// <summary>Two unselected tabs side by side share one divider; each used to draw its own ("││").</summary>
    [TestMethod]
    public async Task AdjacentUnselectedTabsShareOneDivider()
    {
        var theme = CreateMockThemeService().Object;
        var buffer = new MockBuffer(new Size(30, 13));
        var tab = new Tab(theme) { ViewSize = buffer.Size };
        tab.AddTabPage(new TabPage("A", theme));
        tab.AddTabPage(new TabPage("B", theme));
        tab.AddTabPage(new TabPage("C", theme));
        await tab.OnInitializeAsync();

        tab.Draw(buffer);

        var strip = buffer.ContentAsString.Split(Environment.NewLine)[1];
        Assert.IsTrue(strip.StartsWith("║< A >│ B │ C │ ", StringComparison.Ordinal), strip);
        Assert.IsFalse(strip.Contains("││"));
    }
}