using iTerminal.Core.Data;
using iTerminal.Core.Model.Workspaces;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace iTerminal.Core.Services.Workspaces;

/// <summary>Keeps the workspaces as JSON in one setting, the way <c>RemoteConnectionService</c> keeps its connections.</summary>
public sealed class WorkspaceService(
    ISettingsService settingsService,
    IWorkspaceLayoutService layoutService,
    TimeProvider? timeProvider = null) : IWorkspaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly Lock _lock = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public IReadOnlyList<Workspace> GetAll()
    {
        lock (_lock)
        {
            return Read();
        }
    }

    public string? ActiveName
    {
        get
        {
            lock (_lock)
            {
                var name = settingsService.Get(Settings.ActiveWorkspace);
                return !string.IsNullOrEmpty(name) && Read().Any(w => Same(w.Name, name)) ? name : null;
            }
        }
    }

    public WorkspaceLayout CaptureCurrent() => layoutService.Capture();

    public bool IsCurrent(Workspace workspace) => workspace.Layout.SameScreenAs(layoutService.Capture());

    public string? ValidateName(string name, string? renaming = null)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "Give the workspace a name.";
        }

        if (trimmed.Length > IWorkspaceService.MaxNameLength)
        {
            return $"A name is at most {IWorkspaceService.MaxNameLength} characters.";
        }

        if (trimmed.Any(char.IsControl))
        {
            return "A name cannot contain control characters.";
        }

        lock (_lock)
        {
            if (Read().Any(w => Same(w.Name, trimmed) && !(renaming is not null && Same(w.Name, renaming))))
            {
                return $"There is already a workspace called '{trimmed}'.";
            }
        }

        return null;
    }

    public string SuggestName()
    {
        lock (_lock)
        {
            var names = Read().Select(w => w.Name).ToList();
            for (var n = 1; ; n++)
            {
                var candidate = $"Workspace {n}";
                if (!names.Any(name => Same(name, candidate)))
                {
                    return candidate;
                }
            }
        }
    }

    public Workspace Save(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > IWorkspaceService.MaxNameLength || trimmed.Any(char.IsControl))
        {
            throw new InvalidOperationException(ValidateName(trimmed) ?? "The name is not usable.");
        }

        var layout = layoutService.Capture();
        lock (_lock)
        {
            var all = Read().ToList();
            var index = all.FindIndex(w => Same(w.Name, trimmed));
            // An existing one is replaced where it stands, so the 1-9 keys do not move under the person's fingers.
            var saved = new Workspace(index >= 0 ? all[index].Name : trimmed, layout, _time.GetUtcNow().UtcDateTime);
            if (index >= 0)
            {
                all[index] = saved;
            }
            else
            {
                all.Add(saved);
            }

            Write(all);
            settingsService.Set(Settings.ActiveWorkspace, saved.Name);
            return saved;
        }
    }

    public void Rename(string name, string newName)
    {
        var trimmed = (newName ?? string.Empty).Trim();
        if (ValidateName(trimmed, name) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        lock (_lock)
        {
            var all = Read().ToList();
            var index = all.FindIndex(w => Same(w.Name, name));
            if (index < 0)
            {
                throw new InvalidOperationException($"There is no workspace called '{name}'.");
            }

            var wasActive = Same(settingsService.Get(Settings.ActiveWorkspace) ?? string.Empty, all[index].Name);
            all[index] = all[index] with { Name = trimmed };
            Write(all);
            if (wasActive)
            {
                settingsService.Set(Settings.ActiveWorkspace, trimmed);
            }
        }
    }

    public void Delete(string name)
    {
        lock (_lock)
        {
            Write(Read().Where(w => !Same(w.Name, name)).ToList());
        }
    }

    public async Task<IReadOnlyList<string>> RestoreAsync(string name)
    {
        Workspace? workspace;
        lock (_lock)
        {
            workspace = Read().FirstOrDefault(w => Same(w.Name, name));
        }

        if (workspace is null)
        {
            return [$"There is no workspace called '{name}'."];
        }

        var problems = await layoutService.ApplyAsync(workspace.Layout);
        lock (_lock)
        {
            settingsService.Set(Settings.ActiveWorkspace, workspace.Name);
        }

        return problems;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);

    private List<Workspace> Read()
    {
        var json = settingsService.Get(Settings.Workspaces);
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Workspace>>(json, JsonOptions)?
                .Where(w => w is { Name.Length: > 0, Layout: not null })
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Write(List<Workspace> workspaces) =>
        settingsService.Set(Settings.Workspaces, JsonSerializer.Serialize(workspaces, JsonOptions));
}
