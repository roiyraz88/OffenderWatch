import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ApiError } from "../api/client";
import { getRuns } from "../api/runs";
import { getRunComparison } from "../api/runComparison";
import { LoadingSpinner } from "../components/LoadingSpinner";
import { PageLoading } from "../components/PageLoading";
import type { RunSummary } from "../types/run";
import type { ComparisonChangeType, RunComparison } from "../types/runComparison";

function formatTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : "—";
}

function runOptionLabel(run: RunSummary): string {
  return `Run #${run.id} — ${run.environmentNameSnapshot} — ${formatTime(run.createdAtUtc)} — ${run.status}`;
}

type Filter = "all" | "regressions" | "recoveries" | "new" | "missing" | "unchanged";

const FILTERS: { key: Filter; label: string; changes: ComparisonChangeType[] | null }[] = [
  { key: "all", label: "All Changes", changes: null },
  { key: "regressions", label: "Regressions", changes: ["Regression"] },
  { key: "recoveries", label: "Recoveries", changes: ["Recovery"] },
  { key: "new", label: "New", changes: ["New"] },
  { key: "missing", label: "Missing", changes: ["Missing"] },
  { key: "unchanged", label: "Unchanged", changes: ["StillPassing", "StillFailing", "ExpectedFailure", "Unchanged", "OtherChange"] },
];

function changeBadgeClass(change: ComparisonChangeType): string {
  switch (change) {
    case "Regression":
      return "status-badge status-failed";
    case "Recovery":
      return "status-badge status-completed";
    case "New":
      return "status-badge status-running";
    case "Missing":
      return "status-badge status-stopped";
    case "ExpectedFailure":
      return "status-badge status-expectedfail";
    case "StillFailing":
      return "status-badge status-failed";
    case "StillPassing":
      return "status-badge status-completed";
    default:
      return "status-badge status-cancelled";
  }
}

function statusBadge(status: string | null) {
  if (!status) return <span className="mono">—</span>;
  return <span className={`status-badge status-${status.toLowerCase()}`}>{status}</span>;
}

// Bonus B-02 — Run Comparison (Base Run -> Compare Run). Entirely read-only:
// selecting/changing runs here never creates, starts, or modifies any Run.
export function RunComparePage() {
  const [searchParams, setSearchParams] = useSearchParams();

  const [runs, setRuns] = useState<RunSummary[] | null>(null);
  const [runsError, setRunsError] = useState<string | null>(null);

  const urlBase = searchParams.get("base");
  const urlCompare = searchParams.get("compare");

  const [baseRunId, setBaseRunId] = useState<number | null>(urlBase ? Number(urlBase) : null);
  const [compareRunId, setCompareRunId] = useState<number | null>(urlCompare ? Number(urlCompare) : null);

  const [comparison, setComparison] = useState<RunComparison | null>(null);
  const [loadingComparison, setLoadingComparison] = useState(false);
  const [comparisonError, setComparisonError] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>("all");

  useEffect(() => {
    (async () => {
      try {
        setRuns(await getRuns());
      } catch (err) {
        setRunsError(err instanceof ApiError ? err.message : "Could not reach the API.");
      }
    })();
  }, []);

  async function runComparison(base: number, compare: number) {
    setLoadingComparison(true);
    setComparisonError(null);
    try {
      setComparison(await getRunComparison(base, compare));
    } catch (err) {
      setComparison(null);
      setComparisonError(err instanceof ApiError ? err.message : "Could not reach the API.");
    } finally {
      setLoadingComparison(false);
    }
  }

  // A comparison already named in the URL (e.g. a shared/bookmarked link, or
  // "Compare Runs" from the Runs page) loads automatically.
  useEffect(() => {
    if (urlBase && urlCompare && Number(urlBase) !== Number(urlCompare)) {
      runComparison(Number(urlBase), Number(urlCompare));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [urlBase, urlCompare]);

  const sameRunSelected = baseRunId !== null && compareRunId !== null && baseRunId === compareRunId;
  const canCompare = baseRunId !== null && compareRunId !== null && !sameRunSelected;

  function handleCompareClick() {
    if (!canCompare || baseRunId === null || compareRunId === null) return;
    setSearchParams({ base: String(baseRunId), compare: String(compareRunId) });
    runComparison(baseRunId, compareRunId);
  }

  // The backend already orders rows Regression -> Recovery -> New ->
  // Missing -> ... -> Unchanged (7), so "All Changes" surfaces the
  // meaningful ones first without hiding anything (8).
  const visibleTests = useMemo(() => {
    if (!comparison) return [];
    const activeFilter = FILTERS.find((f) => f.key === filter) ?? FILTERS[0];
    if (activeFilter.changes === null) {
      return comparison.tests;
    }
    return comparison.tests.filter((t) => activeFilter.changes!.includes(t.change));
  }, [comparison, filter]);

  return (
    <section>
      <div className="page-header">
        <h1>Compare Runs</h1>
      </div>

      {runsError && <div className="error-banner">{runsError}</div>}

      {!runsError && runs === null && <PageLoading label="Loading runs…" />}

      {runs !== null && (
        <div className="compare-selectors">
          <div className="field">
            <label className="field-label" htmlFor="compare-base-run">
              Base Run
            </label>
            <select
              id="compare-base-run"
              value={baseRunId ?? ""}
              onChange={(e) => setBaseRunId(e.target.value ? Number(e.target.value) : null)}
            >
              <option value="">Select a run…</option>
              {runs.map((run) => (
                <option key={run.id} value={run.id}>
                  {runOptionLabel(run)}
                </option>
              ))}
            </select>
          </div>

          <div className="field">
            <label className="field-label" htmlFor="compare-compare-run">
              Compare Run
            </label>
            <select
              id="compare-compare-run"
              value={compareRunId ?? ""}
              onChange={(e) => setCompareRunId(e.target.value ? Number(e.target.value) : null)}
            >
              <option value="">Select a run…</option>
              {runs.map((run) => (
                <option key={run.id} value={run.id}>
                  {runOptionLabel(run)}
                </option>
              ))}
            </select>
          </div>

          <button className="btn-primary" onClick={handleCompareClick} disabled={!canCompare || loadingComparison}>
            {loadingComparison ? (
              <>
                <LoadingSpinner size="sm" announce={false} />
                Comparing…
              </>
            ) : (
              "Compare"
            )}
          </button>
        </div>
      )}

      {sameRunSelected && <p className="form-error">Base Run and Compare Run must be different runs.</p>}

      {comparisonError && <div className="error-banner">{comparisonError}</div>}

      {comparison && (
        <>
          <div className="compare-direction">
            <span className="compare-run-chip">Run #{comparison.baseRun.id}</span>
            <span aria-hidden="true"> → </span>
            <span className="compare-run-chip">Run #{comparison.compareRun.id}</span>
          </div>

          {comparison.environmentsDiffer && (
            <div className="warning-banner">
              These runs were executed against different environments. Differences may be environment-specific.
            </div>
          )}

          {(comparison.baseRunIncomplete || comparison.compareRunIncomplete) && (
            <div className="warning-banner">
              {comparison.baseRunIncomplete && comparison.compareRunIncomplete
                ? "Both runs are Stopped/Incomplete — this comparison may not represent a complete test suite for either run."
                : comparison.baseRunIncomplete
                  ? "The Base Run is Stopped/Incomplete — this comparison may not represent a complete test suite."
                  : "The Compare Run is Stopped/Incomplete — this comparison may not represent a complete test suite."}
            </div>
          )}

          <div className="compare-meta-grid">
            <div className="compare-meta-card">
              <h3>Base</h3>
              <p className="compare-meta-env">{comparison.baseRun.environmentNameSnapshot}</p>
              <p>
                <span className={`status-badge status-${comparison.baseRun.status.toLowerCase()}`}>
                  {comparison.baseRun.status}
                </span>
              </p>
              <dl className="compare-meta-dl">
                <dt>Trigger</dt>
                <dd>{comparison.baseRun.trigger}</dd>
                <dt>Started</dt>
                <dd>{formatTime(comparison.baseRun.startedAtUtc)}</dd>
                <dt>Ended</dt>
                <dd>{formatTime(comparison.baseRun.endedAtUtc)}</dd>
              </dl>
            </div>
            <div className="compare-meta-card">
              <h3>Compare</h3>
              <p className="compare-meta-env">{comparison.compareRun.environmentNameSnapshot}</p>
              <p>
                <span className={`status-badge status-${comparison.compareRun.status.toLowerCase()}`}>
                  {comparison.compareRun.status}
                </span>
              </p>
              <dl className="compare-meta-dl">
                <dt>Trigger</dt>
                <dd>{comparison.compareRun.trigger}</dd>
                <dt>Started</dt>
                <dd>{formatTime(comparison.compareRun.startedAtUtc)}</dd>
                <dt>Ended</dt>
                <dd>{formatTime(comparison.compareRun.endedAtUtc)}</dd>
              </dl>
            </div>
          </div>

          <div className="compare-summary-cards">
            <div className="compare-summary-card compare-summary-regression">
              <span className="compare-summary-value">{comparison.summary.regressions}</span>
              <span className="compare-summary-label">Regressions</span>
            </div>
            <div className="compare-summary-card compare-summary-recovery">
              <span className="compare-summary-value">{comparison.summary.recoveries}</span>
              <span className="compare-summary-label">Recoveries</span>
            </div>
            <div className="compare-summary-card compare-summary-new">
              <span className="compare-summary-value">{comparison.summary.new}</span>
              <span className="compare-summary-label">New</span>
            </div>
            <div className="compare-summary-card compare-summary-missing">
              <span className="compare-summary-value">{comparison.summary.missing}</span>
              <span className="compare-summary-label">Missing</span>
            </div>
          </div>

          <h2>Totals</h2>
          <table className="env-table compare-totals-table">
            <thead>
              <tr>
                <th>Metric</th>
                <th>Base</th>
                <th>Compare</th>
                <th>Change</th>
              </tr>
            </thead>
            <tbody>
              {(
                [
                  ["Passed", comparison.totalsDelta.passed],
                  ["Failed", comparison.totalsDelta.failed],
                  ["ExpectedFail", comparison.totalsDelta.expectedFail],
                  ["Skipped", comparison.totalsDelta.skipped],
                  ["Total", comparison.totalsDelta.total],
                ] as const
              ).map(([label, metric]) => (
                <tr key={label}>
                  <td>{label}</td>
                  <td>{metric.base}</td>
                  <td>{metric.compare}</td>
                  <td className={metric.delta > 0 ? "compare-delta-up" : metric.delta < 0 ? "compare-delta-down" : ""}>
                    {metric.delta > 0 ? `+${metric.delta}` : metric.delta}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          <div className="page-header">
            <h2>Test Differences</h2>
            <div className="compare-filters">
              {FILTERS.map((f) => (
                <button
                  key={f.key}
                  className={filter === f.key ? "compare-filter-active" : ""}
                  onClick={() => setFilter(f.key)}
                >
                  {f.label}
                </button>
              ))}
            </div>
          </div>

          {visibleTests.length === 0 ? (
            <p>No tests match this filter.</p>
          ) : (
            <table className="env-table">
              <thead>
                <tr>
                  <th>Test</th>
                  <th>Requirement</th>
                  <th>Base Status</th>
                  <th>Compare Status</th>
                  <th>Change</th>
                </tr>
              </thead>
              <tbody>
                {visibleTests.map((t) => (
                  <tr key={t.testCaseId}>
                    <td>
                      <Link to={`/tests/${t.testCaseId}`} className="truncate-cell" title={t.name}>
                        {t.name}
                      </Link>
                    </td>
                    <td>{t.requirementId ?? "—"}</td>
                    <td>{statusBadge(t.baseStatus)}</td>
                    <td>{statusBadge(t.compareStatus)}</td>
                    <td>
                      <span className={changeBadgeClass(t.change)}>{t.change}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </section>
  );
}
