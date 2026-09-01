using OffenderWatch.TestManagement.Server.Models;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>TM-04 (6.3) — pure, deterministic history/transition rules over a chronological ScenarioResult sequence for one TestCase.</summary>
public static class HistoryClassifier
{
    private enum Comparable
    {
        SuccessLike,
        FailureLike,
        Neutral,
    }

    private static Comparable Classify(ScenarioStatus status) => status switch
    {
        ScenarioStatus.Passed => Comparable.SuccessLike,
        ScenarioStatus.Failed or ScenarioStatus.ExpectedFail => Comparable.FailureLike,
        _ => Comparable.Neutral, // Skipped, Cancelled (and, defensively, Queued/Running)
    };

    /// <summary>6.3 — one transition per chronological (oldest-first) entry. Neutral results never update the "previous comparable" state and are themselves reported as Neutral.</summary>
    public static IReadOnlyList<string> ComputeTransitions(IReadOnlyList<ScenarioStatus> chronological)
    {
        var transitions = new List<string>(chronological.Count);
        Comparable? previous = null;

        foreach (var status in chronological)
        {
            var current = Classify(status);
            if (current == Comparable.Neutral)
            {
                transitions.Add("Neutral");
                continue; // skipped when looking for the previous comparable result
            }

            if (previous is null)
            {
                transitions.Add("FirstResult");
            }
            else
            {
                transitions.Add((previous.Value, current) switch
                {
                    (Comparable.SuccessLike, Comparable.FailureLike) => "Regression",
                    (Comparable.FailureLike, Comparable.SuccessLike) => "Recovery",
                    (Comparable.FailureLike, Comparable.FailureLike) => "StillFailing",
                    (Comparable.SuccessLike, Comparable.SuccessLike) => "StillPassing",
                    _ => "Neutral", // unreachable (current is never Neutral here)
                });
            }
            previous = current;
        }

        return transitions;
    }

    /// <summary>
    /// 6.4 — index (into <paramref name="chronological"/>) of the run where the
    /// current continuous failure streak began, or null if the latest
    /// comparable result is not failure-like (including "never executed" /
    /// "only neutral results so far"). Skipped/Cancelled entries never break
    /// or start a streak.
    /// </summary>
    public static int? ComputeCurrentFailureSinceIndex(IReadOnlyList<ScenarioStatus> chronological)
    {
        int? streakStartIndex = null;
        bool latestComparableIsFailure = false;

        for (var i = 0; i < chronological.Count; i++)
        {
            var current = Classify(chronological[i]);
            if (current == Comparable.Neutral)
            {
                continue;
            }

            if (current == Comparable.FailureLike)
            {
                streakStartIndex ??= i;
                latestComparableIsFailure = true;
            }
            else
            {
                streakStartIndex = null;
                latestComparableIsFailure = false;
            }
        }

        return latestComparableIsFailure ? streakStartIndex : null;
    }

    /// <summary>6.5 — index of the most recent Passed entry, or null if the TestCase has never passed.</summary>
    public static int? ComputeLastPassIndex(IReadOnlyList<ScenarioStatus> chronological)
    {
        int? lastPass = null;
        for (var i = 0; i < chronological.Count; i++)
        {
            if (Classify(chronological[i]) == Comparable.SuccessLike)
            {
                lastPass = i;
            }
        }
        return lastPass;
    }

    /// <summary>
    /// 6.6 — flaky when, among the last <paramref name="windowSize"/> comparable
    /// (non-neutral) results, the success/failure classification switches more
    /// than once between consecutive entries. Skipped/Cancelled are ignored
    /// entirely (not counted toward the window, not counted as a switch).
    /// Environment-agnostic by design — this method only ever sees the exact
    /// status sequence it's handed. Callers that want flakiness scoped to a
    /// single Environment (e.g. a controlled/alternate Environment used once
    /// for a Regression/Recovery demonstration must never make the real
    /// target's own consistent history look flaky) filter to that
    /// Environment's own chronological entries *before* calling this.
    /// </summary>
    public static bool ComputeIsFlaky(IReadOnlyList<ScenarioStatus> chronological, int windowSize = 10)
    {
        var comparable = chronological
            .Select(Classify)
            .Where(c => c != Comparable.Neutral)
            .ToList();

        var window = comparable.Skip(Math.Max(0, comparable.Count - windowSize)).ToList();

        var switches = 0;
        for (var i = 1; i < window.Count; i++)
        {
            if (window[i] != window[i - 1])
            {
                switches++;
            }
        }
        return switches > 1;
    }
}
