using iConsole.Data;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A stack of <see cref="Expander"/> sections of which at most one is open at a time: opening a section
/// closes the others, so a long settings page stays one screen tall. The sections are laid out one below
/// the other and re-flowed whenever one opens or closes; <see cref="Control.ViewSize"/> follows the total.
/// </summary>
public class Accordion : Control
{
    private readonly List<Expander> _sections = [];
    private bool _isReflowing;

    public Accordion(IThemeService themeService) : base(themeService)
    {
        ViewSize = new Size(40, 0);
    }

    public IReadOnlyList<Expander> Sections => _sections;

    /// <summary>The open section's index, or -1 when all are closed.</summary>
    public int OpenIndex => _sections.FindIndex(s => s.IsExpanded);

    public Expander AddSection(string header, IControl content, int contentHeight = 3)
    {
        Expander section = new(ThemeService)
        {
            Header = header,
            ContentHeight = contentHeight,
            ViewSize = new Size(ViewSize.Width, 1),
        };
        section.Content = content;
        section.ExpandedChanged += (_, isExpanded) => OnSectionToggled(section, isExpanded);
        _sections.Add(section);
        AddControl(section);
        Reflow();
        return section;
    }

    /// <summary>Opens section <paramref name="index"/>, closing the others; -1 closes all.</summary>
    public void Open(int index)
    {
        for (int i = 0; i < _sections.Count; i++)
        {
            _sections[i].IsExpanded = i == index;
        }
    }

    private void OnSectionToggled(Expander section, bool isExpanded)
    {
        if (_isReflowing)
        {
            return;
        }

        if (isExpanded)
        {
            _isReflowing = true;
            try
            {
                foreach (Expander other in _sections.Where(s => s != section))
                {
                    other.IsExpanded = false;
                }
            }
            finally
            {
                _isReflowing = false;
            }
        }

        Reflow();
    }

    private void Reflow()
    {
        long y = 0;
        foreach (Expander section in _sections)
        {
            section.ViewSize = new Size(ViewSize.Width, section.ViewSize.Height);
            section.Position = new Position(0, y);
            y += section.ViewSize.Height;
        }

        ViewSize = new Size(ViewSize.Width, y);
        InvalidateTree();
        StateHasChanged();
    }
}
