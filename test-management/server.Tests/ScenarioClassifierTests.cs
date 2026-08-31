using OffenderWatch.TestManagement.Server.Models;
using OffenderWatch.TestManagement.Server.Services;
using Xunit;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>Step 4.11/4.25 — expected-failure classification rules.</summary>
public class ScenarioClassifierTests
{
    [Fact]
    public void NormalPass_IsPassed()
    {
        var status = ScenarioClassifier.ClassifyFinalStatus("passed", nativeExpectedFailure: false, hasKnownDefectMetadata: false);
        Assert.Equal(ScenarioStatus.Passed, status);
    }

    [Fact]
    public void FailureWithoutBugMetadata_IsFailed()
    {
        var status = ScenarioClassifier.ClassifyFinalStatus("failed", nativeExpectedFailure: false, hasKnownDefectMetadata: false);
        Assert.Equal(ScenarioStatus.Failed, status);
    }

    [Fact]
    public void FailureWithBugMetadata_IsExpectedFail()
    {
        var status = ScenarioClassifier.ClassifyFinalStatus("failed", nativeExpectedFailure: false, hasKnownDefectMetadata: true);
        Assert.Equal(ScenarioStatus.ExpectedFail, status);
    }

    [Fact]
    public void NativeExpectedFailure_IsExpectedFail_RegardlessOfBugMetadata()
    {
        var status = ScenarioClassifier.ClassifyFinalStatus("failed", nativeExpectedFailure: true, hasKnownDefectMetadata: false);
        Assert.Equal(ScenarioStatus.ExpectedFail, status);
    }

    [Fact]
    public void KnownDefectThatUnexpectedlyPasses_IsStillPassed()
    {
        // Rule 6 — an unexpectedly-passing known-defect scenario is Passed, not ExpectedFail.
        var status = ScenarioClassifier.ClassifyFinalStatus("passed", nativeExpectedFailure: false, hasKnownDefectMetadata: true);
        Assert.Equal(ScenarioStatus.Passed, status);
    }

    [Fact]
    public void Skipped_IsSkipped()
    {
        var status = ScenarioClassifier.ClassifyFinalStatus("skipped", nativeExpectedFailure: false, hasKnownDefectMetadata: false);
        Assert.Equal(ScenarioStatus.Skipped, status);
    }
}
