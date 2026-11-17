using AppForge.Core.Domain;

namespace AppForge.Core.Abstractions;

/// <summary>Eintrag aus HKLM\...\Uninstall (bzw. WOW6432Node) – Basis für Detection/Uninstall-Ableitung.</summary>
public sealed record UninstallEntry(
    string KeyPath,
    string DisplayName,
    string? DisplayVersion,
    string? Publisher,
    string? UninstallString,
    string? QuietUninstallString,
    string? InstallLocation,
    bool Is32BitOn64,
    bool IsMsi);

public sealed record SystemSnapshot(
    IReadOnlyList<UninstallEntry> UninstallEntries,
    IReadOnlyList<string> Services,
    IReadOnlyList<string> Shortcuts,
    IReadOnlyList<string> ProgramFolders);

public sealed record ScenarioOutcome(TestScenario Scenario, TestResult Result, int ExitCode, string Log, IReadOnlyList<string> Screenshots);

public sealed record TestRunReport(
    IReadOnlyList<ScenarioOutcome> Outcomes,
    SystemSnapshot? Before,
    SystemSnapshot? AfterInstall)
{
    public bool AllPassed => Outcomes.Count > 0 && Outcomes.All(o => o.Result is TestResult.Passed or TestResult.Skipped);
}

public sealed record TestRunRequest(
    Guid BuildId,
    string PackageFolder,
    string DetectionScript,
    IReadOnlyList<TestScenario> Scenarios,
    TimeSpan Timeout);

/// <summary>
/// Wegwerf-Testumgebung (Standard Cloud: frische Azure-VM pro Lauf; Option: Windows Sandbox).
/// </summary>
public interface ITestEnvironment
{
    string Name { get; }

    Task<TestRunReport> RunAsync(TestRunRequest request, CancellationToken ct = default);
}
