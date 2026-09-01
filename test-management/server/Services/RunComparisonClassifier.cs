using OffenderWatch.TestManagement.Server.Models;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>Bonus B-02 — how one TestCase changed between a Base Run and a Compare Run.</summary>
public enum ComparisonChangeType
{
    /// <summary>Exists in the Compare Run only.</summary>
    New,

    /// <summary>Existed in the Base Run only.</summary>
    Missing,

    /// <summary>Base was a genuine Passed; Compare is an unexpected Failed.</summary>
    Regression,

    /// <summary>Base was failing (Failed or ExpectedFail — a known defect getting fixed still counts); Compare is Passed.</summary>
    Recovery,

    StillPassing,
    StillFailing,

    /// <summary>Both Base and Compare are ExpectedFail — the same known defect, still known.</summary>
    ExpectedFailure,

    /// <summary>Both sides are the exact same Skipped/Cancelled status (or otherwise identical) — nothing to report.</summary>
    Unchanged,

    /// <summary>
    /// A real status change that is deliberately NOT forced into Regression/
    /// Recovery — e.g. Passed -&gt; ExpectedFail (a newly-known defect, not an
    /// unexpected regression per the assignment's explicit instruction), or
    /// any change involving a Skipped/Cancelled result.
    /// </summary>
    OtherChange,
}

/// <summary>
/// Bonus B-02 — pure, deterministic Base -&gt; Compare classification for one
/// TestCase's two (nullable) statuses. Deliberately a separate, independent
/// class from <see cref="HistoryClassifier"/> (TM-04): B-02 compares exactly
/// two specific runs in one fixed direction, while TM-04 classifies an
/// entire chronological history — the two are semantically different
/// operations and must not be merged, so that neither can accidentally
/// change the other's behavior.
/// </summary>
public static class RunComparisonClassifier
{
    private static bool IsNeutral(ScenarioStatus status) =>
        status is ScenarioStatus.Skipped or ScenarioStatus.Cancelled or ScenarioStatus.Queued or ScenarioStatus.Running;

    /// <summary>
    /// <paramref name="baseStatus"/> null means the TestCase did not run in
    /// the Base Run (-&gt; New); <paramref name="compareStatus"/> null means it
    /// did not run in the Compare Run (-&gt; Missing). Never both null — the
    /// caller only classifies TestCases that appear in at least one run.
    /// </summary>
    public static ComparisonChangeType Classify(ScenarioStatus? baseStatus, ScenarioStatus? compareStatus)
    {
        if (baseStatus is null)
        {
            return ComparisonChangeType.New;
        }
        if (compareStatus is null)
        {
            return ComparisonChangeType.Missing;
        }

        var b = baseStatus.Value;
        var c = compareStatus.Value;

        // Skipped/Cancelled (and, defensively, Queued/Running) are never
        // comparable outcomes — a Skipped result carries no pass/fail
        // information, so it must never manufacture a false
        // Regression/Recovery (9's explicit requirement).
        if (IsNeutral(b) || IsNeutral(c))
        {
            return b == c ? ComparisonChangeType.Unchanged : ComparisonChangeType.OtherChange;
        }

        return (b, c) switch
        {
            (ScenarioStatus.Passed, ScenarioStatus.Passed) => ComparisonChangeType.StillPassing,
            (ScenarioStatus.Passed, ScenarioStatus.Failed) => ComparisonChangeType.Regression,
            // A Passed test becoming a known ExpectedFail is a real change
            // worth surfacing, but per the assignment it must NOT
            // automatically read as an unexpected regression.
            (ScenarioStatus.Passed, ScenarioStatus.ExpectedFail) => ComparisonChangeType.OtherChange,

            (ScenarioStatus.Failed, ScenarioStatus.Passed) => ComparisonChangeType.Recovery,
            (ScenarioStatus.Failed, ScenarioStatus.Failed) => ComparisonChangeType.StillFailing,
            (ScenarioStatus.Failed, ScenarioStatus.ExpectedFail) => ComparisonChangeType.OtherChange,

            // The known defect got fixed — still a genuine Recovery.
            (ScenarioStatus.ExpectedFail, ScenarioStatus.Passed) => ComparisonChangeType.Recovery,
            // Was already failing (as a known defect); still failing, just
            // reclassified as unexpected — not a Regression, since the Base
            // Run was never Passed.
            (ScenarioStatus.ExpectedFail, ScenarioStatus.Failed) => ComparisonChangeType.OtherChange,
            (ScenarioStatus.ExpectedFail, ScenarioStatus.ExpectedFail) => ComparisonChangeType.ExpectedFailure,

            _ => ComparisonChangeType.OtherChange,
        };
    }
}
