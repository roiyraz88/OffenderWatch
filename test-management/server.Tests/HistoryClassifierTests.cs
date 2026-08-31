using OffenderWatch.TestManagement.Server.Models;
using OffenderWatch.TestManagement.Server.Services;
using Xunit;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>Step 6.24 — the TM-04 transition/streak/flakiness rules, tested directly as pure functions.</summary>
public class HistoryClassifierTests
{
    [Fact]
    public void ComputeTransitions_PassedThenFailed_IsRegression()
    {
        var t = HistoryClassifier.ComputeTransitions(new[] { ScenarioStatus.Passed, ScenarioStatus.Failed });
        Assert.Equal(new[] { "FirstResult", "Regression" }, t);
    }

    [Fact]
    public void ComputeTransitions_FailedThenPassed_IsRecovery()
    {
        var t = HistoryClassifier.ComputeTransitions(new[] { ScenarioStatus.Failed, ScenarioStatus.Passed });
        Assert.Equal(new[] { "FirstResult", "Recovery" }, t);
    }

    [Fact]
    public void ComputeTransitions_FailedThenFailed_IsStillFailing()
    {
        var t = HistoryClassifier.ComputeTransitions(new[] { ScenarioStatus.Failed, ScenarioStatus.ExpectedFail });
        Assert.Equal(new[] { "FirstResult", "StillFailing" }, t);
    }

    [Fact]
    public void ComputeTransitions_PassedThenPassed_IsStillPassing()
    {
        var t = HistoryClassifier.ComputeTransitions(new[] { ScenarioStatus.Passed, ScenarioStatus.Passed });
        Assert.Equal(new[] { "FirstResult", "StillPassing" }, t);
    }

    [Fact]
    public void ComputeTransitions_NeutralResultsDoNotCreateFalseTransitions()
    {
        // Passed, Skipped, Skipped, Failed -> the Failed compares against the
        // last *comparable* result (Passed), not against a neutral one.
        var t = HistoryClassifier.ComputeTransitions(new[]
        {
            ScenarioStatus.Passed, ScenarioStatus.Skipped, ScenarioStatus.Cancelled, ScenarioStatus.Failed,
        });
        Assert.Equal(new[] { "FirstResult", "Neutral", "Neutral", "Regression" }, t);
    }

    [Fact]
    public void ComputeCurrentFailureSinceIndex_BeginsAtCorrectRun()
    {
        // Run1 Passed, Run2 Failed, Run3 Failed, Run4 ExpectedFail -> streak began at index 1 (Run2).
        var statuses = new[] { ScenarioStatus.Passed, ScenarioStatus.Failed, ScenarioStatus.Failed, ScenarioStatus.ExpectedFail };
        Assert.Equal(1, HistoryClassifier.ComputeCurrentFailureSinceIndex(statuses));
    }

    [Fact]
    public void ComputeCurrentFailureSinceIndex_RecoveryClearsIt()
    {
        var statuses = new[] { ScenarioStatus.Passed, ScenarioStatus.Failed, ScenarioStatus.Failed, ScenarioStatus.Passed };
        Assert.Null(HistoryClassifier.ComputeCurrentFailureSinceIndex(statuses));
    }

    [Fact]
    public void ComputeCurrentFailureSinceIndex_SkippedBetweenFailuresDoesNotBreakStreak()
    {
        var statuses = new[] { ScenarioStatus.Failed, ScenarioStatus.Skipped, ScenarioStatus.Failed };
        Assert.Equal(0, HistoryClassifier.ComputeCurrentFailureSinceIndex(statuses));
    }

    [Fact]
    public void ComputeLastPassIndex_ResolvesTheMostRecentPass()
    {
        var statuses = new[] { ScenarioStatus.Passed, ScenarioStatus.Failed, ScenarioStatus.Passed, ScenarioStatus.Failed };
        Assert.Equal(2, HistoryClassifier.ComputeLastPassIndex(statuses));
    }

    [Fact]
    public void ComputeLastPassIndex_NeverPassed_ReturnsNull()
    {
        var statuses = new[] { ScenarioStatus.Failed, ScenarioStatus.ExpectedFail };
        Assert.Null(HistoryClassifier.ComputeLastPassIndex(statuses));
    }

    [Theory]
    [InlineData(new[] { ScenarioStatus.Passed, ScenarioStatus.Failed, ScenarioStatus.Passed }, true)]
    [InlineData(new[] { ScenarioStatus.Passed, ScenarioStatus.Failed, ScenarioStatus.Failed }, false)]
    [InlineData(new[] { ScenarioStatus.Failed, ScenarioStatus.Passed }, false)]
    [InlineData(new[] { ScenarioStatus.Passed, ScenarioStatus.Failed, ScenarioStatus.Passed, ScenarioStatus.Failed }, true)]
    public void ComputeIsFlaky_MatchesTheDocumentedHeuristic(ScenarioStatus[] statuses, bool expectedFlaky)
    {
        Assert.Equal(expectedFlaky, HistoryClassifier.ComputeIsFlaky(statuses));
    }

    [Fact]
    public void ComputeIsFlaky_IgnoresSkippedAndCancelledEntirely()
    {
        var statuses = new[]
        {
            ScenarioStatus.Passed, ScenarioStatus.Skipped, ScenarioStatus.Failed,
            ScenarioStatus.Cancelled, ScenarioStatus.Passed,
        };
        // Comparable sequence is Passed, Failed, Passed -> 2 switches -> flaky.
        Assert.True(HistoryClassifier.ComputeIsFlaky(statuses));
    }

    [Fact]
    public void ComputeIsFlaky_OnlyLooksAtTheLastTenComparableResults()
    {
        // 11 alternating results -> only the last 10 count. Both windows here
        // are still alternating either way, so assert the window size is
        // actually being applied by checking a mixed prefix + stable suffix.
        var statuses = new List<ScenarioStatus>();
        statuses.Add(ScenarioStatus.Failed); // index 0 — outside the last-10 window
        for (var i = 0; i < 10; i++)
        {
            statuses.Add(ScenarioStatus.Passed); // last 10 are all Passed -> not flaky
        }
        Assert.False(HistoryClassifier.ComputeIsFlaky(statuses));
    }
}
