using OffenderWatch.TestManagement.Server.Models;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// The 4.11 expected-failure classification rules, pulled out as a small
/// pure function so they're directly unit-testable without spawning a
/// runner process.
/// </summary>
public static class ScenarioClassifier
{
    public static ScenarioStatus ClassifyFinalStatus(string? runnerStatus, bool nativeExpectedFailure, bool hasKnownDefectMetadata)
    {
        if (nativeExpectedFailure)
        {
            return ScenarioStatus.ExpectedFail; // rules 4/5 — native xfail / expected-failure semantics
        }

        return runnerStatus switch
        {
            "failed" => hasKnownDefectMetadata ? ScenarioStatus.ExpectedFail : ScenarioStatus.Failed, // rules 2/3
            "skipped" => ScenarioStatus.Skipped,
            "passed" => ScenarioStatus.Passed, // rule 1, and rule 6 (an unexpectedly-passing known defect is still Passed)
            _ => ScenarioStatus.Passed,
        };
    }
}
