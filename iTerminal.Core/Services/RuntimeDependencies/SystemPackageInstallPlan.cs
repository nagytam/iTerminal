namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>What a program is called in each package manager, for programs the system installs rather than iTerminal.</summary>
internal sealed record SystemPackageNames(
    string WinGet,
    string Homebrew,
    string Apt,
    string Dnf,
    string Zypper,
    string Pacman,
    string Apk);

/// <summary>
/// How to install a package with the package manager of this system: a program, its arguments and its
/// environment. Windows has winget, which asks for elevation itself. The Linux managers need root: when this
/// process is not root the command runs under <c>sudo -n</c> (no password prompt, since a prompt has no terminal to
/// appear on here), and a failure says what to run by hand.
/// </summary>
internal sealed record SystemPackageInstallPlan(
    string DisplayName,
    string Program,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment,
    bool RequiresSudo,
    string? ManualCommand = null)
{
    public string CommandLine => string.Join(' ', new[] { Program }.Concat(Arguments));

    public static SystemPackageInstallPlan? For(
        SystemPackageNames names,
        bool isWindows,
        bool isMac,
        bool isPrivileged,
        Func<string, string?> findOnPath)
    {
        if (isWindows)
        {
            return findOnPath("winget") is null
                ? null
                : new SystemPackageInstallPlan(
                    "WinGet",
                    "winget",
                    ["install", "--id", names.WinGet, "-e", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
                    null,
                    false);
        }

        if (isMac)
        {
            return findOnPath("brew") is null
                ? null
                : new SystemPackageInstallPlan("Homebrew", "brew", ["install", names.Homebrew], null, false);
        }

        (string Display, string Tool, string[] Arguments, bool NonInteractive)[] candidates =
        [
            ("APT", "apt-get", ["install", "-y", names.Apt], true),
            ("DNF", "dnf", ["install", "-y", names.Dnf], false),
            ("Zypper", "zypper", ["--non-interactive", "install", names.Zypper], false),
            ("Pacman", "pacman", ["-S", "--noconfirm", names.Pacman], false),
            ("APK", "apk", ["add", names.Apk], false),
        ];
        foreach ((string display, string tool, string[] arguments, bool nonInteractive) in candidates)
        {
            if (findOnPath(tool) is null)
            {
                continue;
            }

            Dictionary<string, string>? environment = nonInteractive ? new() { ["DEBIAN_FRONTEND"] = "noninteractive" } : null;
            if (isPrivileged || findOnPath("sudo") is null)
            {
                return new SystemPackageInstallPlan(display, tool, arguments, environment, false);
            }

            return new SystemPackageInstallPlan(
                display,
                "sudo",
                ["-n", .. (nonInteractive ? new[] { "env", "DEBIAN_FRONTEND=noninteractive" } : []), tool, .. arguments],
                null,
                true,
                $"sudo {tool} {string.Join(' ', arguments)}");
        }

        return null;
    }
}
