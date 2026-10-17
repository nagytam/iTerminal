using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using Renci.SshNet;
using Renci.SshNet.Common;
using Serilog;
using System.Globalization;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Services;

public partial class SshFileSystemService : ISshFileSystemService
{
    [GeneratedRegex("[\\w-]{10}[ ]*[0-9]*[ ]*[\\w]*[ ]*[\\w]*[ ]*(?<size>[0-9]*)[ ]*(?<date>[\\w]{3}[ ]*[0-9]*)[ ]*(?<time>[0-9]*:[0-9]*)[ ]*(?<name>[\\w-?.]*)")]
    private static partial Regex RegexLs();
    [GeneratedRegex("(.*)  (.*)  (.*)  (?<free>.*)  (.*) \\/")]
    private static partial Regex RegexDf();

    private ILogger _logger { get; set; }
    public bool IsReadOnly => false;
    private SshClient? _client { get; set; } = null;
    private Url _url { get; set; } = null!;
    private string _host { get; set; } = string.Empty;
    private string _username { get; set; } = string.Empty;
    private AuthenticationMethod[] _authMethods { get; set; } = [];
    public bool IsConnected => _client?.IsConnected ?? false;

    private readonly ITrustedHostStore _knownHosts;
    private string? _hostKeyProblem;

    public SshFileSystemService(ILogger logger, ITrustedHostStore? knownHosts = null)
    {
        _logger = logger;
        _knownHosts = knownHosts ?? new TrustedHostStore();
    }

    public void Connect(string host, string username, string? password, string? privateKeyFilePath)
    {
        _host = host;
        _username = username;
        _authMethods = BuildAuthenticationMethods(username, password, privateKeyFilePath);
        if (_client is null)
        {
            _client = new SshClient(new ConnectionInfo(host, username, _authMethods));
            _client.HostKeyReceived += OnHostKeyReceived;
        }
        if (!_client.IsConnected)
        {
            ConnectVerified(_client.Connect);
        }
        _url = new Url(Container.SSH + username + "@" + host + ":/");
    }

    /// <summary>Trust on first use: a new host key is remembered, a different one for a known host is refused.</summary>
    private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        var connection = sender is BaseClient client ? client.ConnectionInfo : null;
        var hostId = $"ssh:{connection?.Host ?? _host}:{connection?.Port ?? 22}";
        var fingerprint = $"{e.HostKeyName} SHA256:{e.FingerPrintSHA256.TrimEnd('=')}";
        switch (_knownHosts.Check(hostId, fingerprint, out var recorded))
        {
            case HostTrust.Changed:
                _hostKeyProblem = $"The SSH host key of {_host} has changed. Expected {recorded}, the server presented {fingerprint}. "
                    + $"This can mean the server was reinstalled, or that someone is intercepting the connection. Connection refused; forget {hostId} to accept the new key.";
                _logger.Warning("SSH host key mismatch for {Host}: expected {Expected}, got {Actual}", hostId, recorded, fingerprint);
                e.CanTrust = false;
                break;
            case HostTrust.New:
                _logger.Information("SSH host key of {Host} recorded: {Fingerprint}", hostId, fingerprint);
                e.CanTrust = true;
                break;
            default:
                e.CanTrust = true;
                break;
        }
    }

    /// <summary>Runs a connect and, when the host key was the reason it failed, says so instead of SSH.NET's generic message.</summary>
    private void ConnectVerified(Action connect)
    {
        _hostKeyProblem = null;
        try
        {
            connect();
        }
        catch (Exception) when (_hostKeyProblem is not null)
        {
            throw new IOException(_hostKeyProblem);
        }
    }

    private static AuthenticationMethod[] BuildAuthenticationMethods(string username, string? password, string? privateKeyFilePath)
    {
        List<AuthenticationMethod> authMethods = [];
        if (!string.IsNullOrEmpty(privateKeyFilePath) && File.Exists(privateKeyFilePath))
        {
            authMethods.Add(new PrivateKeyAuthenticationMethod(username, new PrivateKeyFile(privateKeyFilePath)));
        }
        if (!string.IsNullOrEmpty(password))
        {
            authMethods.Add(new PasswordAuthenticationMethod(username, password));
        }
        if (authMethods.Count == 0)
        {
            throw new IOException("No SSH credentials supplied: both password and private-key path were empty (or the key file does not exist).");
        }
        return [.. authMethods];
    }

    public ScpClient ScpConnect()
    {
        var scpClient = new ScpClient(new ConnectionInfo(_host, _username, _authMethods), RemotePathTransformation.ShellQuote);
        scpClient.HostKeyReceived += OnHostKeyReceived;
        if (!scpClient.IsConnected)
        {
            ConnectVerified(scpClient.Connect);
        }
        return scpClient;
    }

    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        if (_client is not null)
        {
            var noClobber = overwrite ? string.Empty : " -n";
            using SshCommand cmd = _client.RunCommand($"cp -r{noClobber} {sourceUrl.Path} {targetUrl.Path}");
            if (cmd.ExitStatus != 0)
            {
                throw new IOException($"SSH copy failed: {cmd.Error}");
            }
        }
        return Task.CompletedTask;
    }

    public bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        if (_client is not null)
        {
            using SshCommand cmd = _client.RunCommand($"rm {url.Path}");
            return cmd.ExitStatus == 0;
        }
        return false;
    }

    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin)
    {
        if (_client is not null)
        {
            var isRecursive = (recursive) ? " -R" : string.Empty;
            using SshCommand cmd = _client.RunCommand($"rm {url.Path}{isRecursive}");
            return cmd.ExitStatus == 0;
        }
        return false;
    }

    public void Disconnect()
    {
        _client?.Disconnect();
        _client?.Dispose();
        _client = null;
    }

    public void Dispose()
    {
        if (_client is not null)
        {
            Disconnect();
        }
    }

    public string GetAvailableDiskSpace()
    {
        if (_client is not null)
        {
            try
            {
                using SshCommand cmd = _client.RunCommand("df -h");
                var diskSpace = cmd.Result.Split("\n").First(l => l.EndsWith("/"));
                var regex = RegexDf();
                var match = regex.Match(diskSpace);
                if (match.Success)
                {
                    var freeDiskSpace = match.Groups["free"].Value;
                    return freeDiskSpace;
                }
            }
            catch
            {
                _logger.Error("GetAvailableDiskSpace error");
            }
        }
        return string.Empty;
    }

    public IEnumerable<IEntry> List(Url url)
    {
        List<FileSystemEntry> entries = [];
        if (_client is not null)
        {
            try
            {
                using SshCommand cmd = _client.RunCommand("ls -la " + url.Path);
                var listEntries = cmd.Result.Split("\n");
                var regex = RegexLs();
                foreach (var listEntry in listEntries)
                {
                    var match = regex.Match(listEntry);
                    if (match.Success)
                    {
                        var name = match.Groups[4].Value;
                        if (url.Path == "/" && name == "..")
                        {
                            continue;
                        }
                        if (name != ".")
                        {
                            var folderPostFix = listEntry.StartsWith("d") ? "/" : string.Empty;
                            var size = match.Groups[1].Value.ToFileSize(); ;
                            var date = match.Groups[2].Value.Replace("  ", " ");
                            var time = match.Groups[3].Value;
                            var dateTime = DateTime.ParseExact(
                                date + " " + time,
                                "MMM d HH:mm",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None);
                            var displayName = name;
                            // name = (name == "..") ? string.Empty : name;
                            var fullUrl = url.FullUrl;
                            Url childUrl;
                            if (displayName == ".." && _url.Path == "/")
                            {
                                childUrl = new Url(url.ParentUrl, displayName);
                            }
                            else
                            {
                                childUrl = new Url(url.FullUrl + name + folderPostFix, displayName);
                            }
                            var entry = new FileSystemEntry(childUrl)
                            {
                                FileSize = (childUrl.PathType == PathType.File) ? size : Url.Folder,
                                Date = dateTime.ToString("yyyy-MM-dd"),
                                Time = dateTime.ToString("HH:mm")
                            };
                            entries.Add(entry);
                        }
                    }
                }

                entries = [.. entries
                        .OrderBy(a => a.Url.PathType)
                        .ThenBy(a => a.Url.DisplayName)
                    ];
            }
            catch
            {
                _logger.Error("List error");
            }
        }
        return entries;
    }

    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        if (_client is not null)
        {
            var noClobber = overwrite ? string.Empty : " -n";
            using SshCommand cmd = _client.RunCommand($"mv{noClobber} {sourceUrl.Path} {targetUrl.Path}");
            if (cmd.ExitStatus != 0)
            {
                throw new IOException($"SSH move failed: {cmd.Error}");
            }
        }
        return Task.CompletedTask;
    }

    public Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        using var scpClient = ScpConnect();
        using MemoryStream memoryStream = new(); 
        scpClient.Download(url.Path, memoryStream);
        var memoryFile = new MemoryFile(url, memoryStream.ToArray());
        return Task.FromResult<IMemoryFile?>(memoryFile);
    }

    public Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        using var scpClient = ScpConnect();
        scpClient.Upload(inputStream, url.Path);
        return Task.CompletedTask;
    }

    public bool ExistsFile(Url url)
    {
        if (_client is null)
        {
            return false;
        }
        using SshCommand cmd = _client.RunCommand($"test -f {url.Path} && echo 1 || echo 0");
        return cmd.Result.Trim() == "1";
    }

    public bool ExistsFolder(Url url)
    {
        if (_client is null)
        {
            return false;
        }
        using SshCommand cmd = _client.RunCommand($"test -d {url.Path} && echo 1 || echo 0");
        return cmd.Result.Trim() == "1";
    }

    public async Task CreateFolderAsync(Url url)
    {
        var folderName = url.Directory;
        using SshCommand command = _client!.CreateCommand("mkdir " + url.Path);
        await command.ExecuteAsync(CancellationToken.None);
    }

    public Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var scpClient = ScpConnect();
            void OnDownloading(object? sender, ScpDownloadEventArgs e) => progress?.Report(e.Downloaded);
            scpClient.Downloading += OnDownloading;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                scpClient.Download(url.Path, destination);
            }
            finally
            {
                scpClient.Downloading -= OnDownloading;
            }
        }, cancellationToken);
    }

    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var scpClient = ScpConnect();
            void OnUploading(object? sender, ScpUploadEventArgs e) => progress?.Report(e.Uploaded);
            scpClient.Uploading += OnUploading;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                scpClient.Upload(source, url.Path);
            }
            finally
            {
                scpClient.Uploading -= OnUploading;
            }
        }, cancellationToken);
    }

    public void Execute(ITerminalCommand command)
    {
        if (_client is not null)
        {
            using SshCommand cmdEnv = _client.RunCommand($"env");
            command.EnvironmentVariables = cmdEnv.Result.Replace("\n", Environment.NewLine);

            using SshCommand cmd = _client.RunCommand($"cd {command.WorkingDirectoryUrl.Path};"
                + command.Command);
            command.Output = cmd.Result.Replace("\n", Environment.NewLine);
            command.Error = cmd.Error.Replace("\n", Environment.NewLine);
            command.ExitCode = cmd.ExitStatus ?? int.MinValue;
            command.Host = _host;
            command.Username = _username;
        }
    }
}
