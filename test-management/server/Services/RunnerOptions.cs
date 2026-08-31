namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// Bound from appsettings.json's "Runner" section (4.15) — never a
/// hard-coded absolute path in code. All paths here are relative; the
/// orchestrator resolves them against the server's ContentRootPath at
/// runtime, so the same config works regardless of the current working
/// directory the server process happened to be launched from.
/// </summary>
public class RunnerOptions
{
    /// <summary>Repo root, relative to test-management/server (its ContentRootPath).</summary>
    public string RepoRootRelativeToContentRoot { get; set; } = "../..";

    public string PythonExecutable { get; set; } = "python";
    public string PytestWorkingDirectory { get; set; } = "automation/api";
    public string PytestArguments { get; set; } = "-m pytest -v";

    /// <summary>
    /// Windows-specific default (the ".cmd" shim npm installs). This is a
    /// config value, not a hard-coded repo path — swap it for a
    /// platform-appropriate value (e.g. "node_modules/.bin/playwright") to
    /// run on macOS/Linux.
    /// </summary>
    public string PlaywrightExecutableRelativePath { get; set; } = "node_modules/.bin/playwright.cmd";
    public string PlaywrightWorkingDirectory { get; set; } = "automation/ui";
    public string PlaywrightArguments { get; set; } = "test";
}
