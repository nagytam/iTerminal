using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model;
using iTerminal.Core.Model.Workspaces;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.Core.Services.Workspaces;

/// <summary>
/// Reads the layout from the two panels and the bottom bar, and puts it back the way the settings page
/// and the swap command do: the panel's own sort setting first, then the location.
/// </summary>
public sealed class WorkspaceLayoutService(
    IContentModelService contentModelService,
    ISettingsService settingsService,
    IServiceProvider serviceProvider) : IWorkspaceLayoutService
{
    // Resolved on demand: Content depends, through the AI bar, on services that depend on this one's callers.
    private Content ContentWindow => serviceProvider.GetRequiredService<Content>();

    public WorkspaceLayout Capture()
    {
        var left = Model(ContentModelService.Left);
        var right = Model(ContentModelService.Right);
        var bar = serviceProvider.GetService<Content>()?.CurrentBar ?? WorkspaceBar.Ai;
        var focused = contentModelService.GetLastContentModel()?.Name == ContentModelService.Right
            ? ContentModelService.Right
            : ContentModelService.Left;
        return new WorkspaceLayout(
            left?.Url?.FullUrl ?? string.Empty,
            right?.Url?.FullUrl ?? string.Empty,
            SortOf(left, ContentModelService.Left),
            SortOf(right, ContentModelService.Right),
            bar,
            focused);
    }

    public async Task<IReadOnlyList<string>> ApplyAsync(WorkspaceLayout layout)
    {
        List<string> problems = [];
        await ApplyPanelAsync(ContentModelService.Left, "Left panel", layout.LeftUrl, layout.LeftSort, problems);
        await ApplyPanelAsync(ContentModelService.Right, "Right panel", layout.RightUrl, layout.RightSort, problems);

        IContent content = ContentWindow;
        switch (layout.Bar)
        {
            case WorkspaceBar.FullTerminal:
                content.ShowFullTerminal();
                break;
            case WorkspaceBar.Terminal:
                content.ShowTerminalBar();
                break;
            default:
                content.ShowAiBar();
                break;
        }

        // The bars take the focus when they open; the person's cursor goes back to the panel it was in.
        if (layout.Bar != WorkspaceBar.FullTerminal)
        {
            if (layout.FocusedPanel == ContentModelService.Right)
            {
                content.FocusRightPanel();
            }
            else
            {
                content.FocusLeftPanel();
            }
        }

        return problems;
    }

    private async Task ApplyPanelAsync(string panel, string label, string url, string sort, List<string> problems)
    {
        var parsedSort = PanelSort.Parse(sort);
        settingsService.Set(PanelSort.SettingsKey(panel), parsedSort.Serialize());

        if (url.Length > 0)
        {
            var target = new Url(url);
            if (target.ContainerType == ContainerType.FileSystem && !Directory.Exists(target.Path) && !File.Exists(target.Path))
            {
                problems.Add($"{label}: {target.Path} no longer exists.");
            }
            else
            {
                try
                {
                    await contentModelService.SetUrlAsync(panel, target);
                }
                catch (Exception exception)
                {
                    problems.Add($"{label}: could not open {url} ({exception.Message}).");
                }
            }
        }

        if (Model(panel) is ISortableContentModel sortable)
        {
            sortable.SetSort(parsedSort);
        }
    }

    private IContentModel? Model(string name) =>
        contentModelService.ContentModels.AsSnapshot().FirstOrDefault(m => m.Name == name);

    private string SortOf(IContentModel? model, string panel) =>
        model is ISortableContentModel sortable
            ? sortable.Sort.Serialize()
            : PanelSort.Parse(settingsService.Get(PanelSort.SettingsKey(panel))).Serialize();
}
