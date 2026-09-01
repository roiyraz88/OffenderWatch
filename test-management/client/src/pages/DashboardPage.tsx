import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ApiError } from "../api/client";
import { getDashboard } from "../api/dashboard";
import { PassRateTrendChart } from "../components/PassRateTrendChart";
import { LoadingSpinner } from "../components/LoadingSpinner";
import { PageLoading } from "../components/PageLoading";
import type { Dashboard } from "../types/dashboard";

function formatTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : "—";
}

function formatDuration(seconds: number | null): string {
  if (seconds === null) return "—";
  if (seconds < 60) return `${seconds.toFixed(1)}s`;
  const m = Math.floor(seconds / 60);
  const s = Math.round(seconds % 60);
  return `${m}m ${s}s`;
}

function formatPassRate(rate: number | null): string {
  return rate === null ? "No data" : `${rate.toFixed(1)}%`;
}

function formatSince(seconds: number | null): string {
  if (seconds === null) return "—";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? "" : "s"}`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} hour${hours === 1 ? "" : "s"}`;
  const days = Math.round(hours / 24);
  return `${days} day${days === 1 ? "" : "s"}`;
}

const DECISION_LABEL: Record<string, string> = {
  Go: "Go",
  NoGo: "No-Go",
  Incomplete: "Incomplete",
  NoData: "No Data",
};

const DECISION_EXPLANATION: Record<string, string> = {
  Go: "The latest run completed with zero unexpected failures.",
  NoGo: "The latest run has unexpected failures, or failed to run at all.",
  Incomplete: "The latest run was stopped, or hasn't finished yet.",
  NoData: "No run has been recorded yet.",
};

// TM-07 — the dynamic Dashboard (Step 8). Everything shown here comes
// straight from GET /api/dashboard — no client-side recomputation of
// pass rate, history, or the release decision.
export function DashboardPage() {
  const navigate = useNavigate();
  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      setDashboard(await getDashboard());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not reach the API.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  if (loading && !dashboard) {
    return <PageLoading label="Loading dashboard…" />;
  }

  if (error) {
    return (
      <div className="error-banner">
        <p>{error}</p>
        <button onClick={load} disabled={loading}>
          {loading ? (
            <>
              <LoadingSpinner size="sm" announce={false} />
              Retrying…
            </>
          ) : (
            "Retry"
          )}
        </button>
      </div>
    );
  }

  if (!dashboard) {
    return null;
  }

  return (
    <section>
      <div className="page-header">
        <h1>Dashboard</h1>
        <button onClick={load} disabled={loading}>
          {loading ? (
            <>
              <LoadingSpinner size="sm" announce={false} />
              Refreshing…
            </>
          ) : (
            "Refresh"
          )}
        </button>
      </div>

      <div className={`decision-banner decision-${dashboard.overallDecision.toLowerCase()}`}>
        <div className="decision-label">{DECISION_LABEL[dashboard.overallDecision]}</div>
        <div className="decision-details">
          <p>{DECISION_EXPLANATION[dashboard.overallDecision]}</p>
          <dl className="decision-stats">
            <dt>Latest run</dt>
            <dd>
              {dashboard.latestRelevantRunId ? (
                <Link to={`/runs/${dashboard.latestRelevantRunId}`}>Run #{dashboard.latestRelevantRunId}</Link>
              ) : (
                "—"
              )}
            </dd>
            <dt>Pass rate</dt>
            <dd>{formatPassRate(dashboard.latestRunPassRate)}</dd>
            <dt>Unexpected failures</dt>
            <dd>{dashboard.latestRunUnexpectedFailedCount}</dd>
            <dt>Expected failures</dt>
            <dd>{dashboard.latestRunExpectedFailedCount}</dd>
            <dt>Currently failing tests</dt>
            <dd>{dashboard.currentlyFailingTestCount}</dd>
          </dl>
        </div>
      </div>

      <h2>Latest Run per Environment</h2>
      {dashboard.latestRunsByEnvironment.length === 0 ? (
        <p>No completed runs yet.</p>
      ) : (
        <table className="env-table">
          <thead>
            <tr>
              <th>Environment</th>
              <th>Run</th>
              <th>Status</th>
              <th>Date</th>
              <th>Duration</th>
              <th>Pass Rate</th>
              <th>Passed</th>
              <th>Failed</th>
              <th>Expected Fail</th>
              <th>Skipped</th>
            </tr>
          </thead>
          <tbody>
            {dashboard.latestRunsByEnvironment.map((r) => (
              <tr key={r.runId} className="run-row" onClick={() => navigate(`/runs/${r.runId}`)}>
                <td>{r.environmentNameSnapshot}</td>
                <td>
                  <Link to={`/runs/${r.runId}`} onClick={(e) => e.stopPropagation()}>
                    #{r.runId}
                  </Link>
                </td>
                <td>
                  <span className={`status-badge status-${r.status.toLowerCase()}`}>{r.status}</span>
                </td>
                <td>{formatTime(r.startedAtUtc)}</td>
                <td>{formatDuration(r.durationSeconds)}</td>
                <td>{formatPassRate(r.passRate)}</td>
                <td>{r.passedCount}</td>
                <td>{r.failedCount}</td>
                <td>{r.expectedFailedCount}</td>
                <td>{r.skippedCount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2>Pass-Rate Trend</h2>
      <PassRateTrendChart points={dashboard.passRateTrend} />

      <h2>Currently Failing Tests</h2>
      {dashboard.currentlyFailingTests.length === 0 ? (
        <p>No currently failing tests.</p>
      ) : (
        <table className="env-table">
          <thead>
            <tr>
              <th>Test</th>
              <th>Suite</th>
              <th>Requirement</th>
              <th>Bug</th>
              <th>Status</th>
              <th>Environment</th>
              <th>Failing Since</th>
              <th>Duration</th>
            </tr>
          </thead>
          <tbody>
            {dashboard.currentlyFailingTests.map((t) => (
              <tr key={t.testCaseId}>
                <td className="mono">
                  <Link to={`/tests/${t.testCaseId}`} className="truncate-cell" title={t.name}>
                    {t.name}
                  </Link>
                </td>
                <td>{t.suite}</td>
                <td>{t.requirementId ?? "—"}</td>
                <td>{t.bugId ?? "—"}</td>
                <td>
                  <span className={`status-badge status-${t.currentStatus.toLowerCase()}`}>{t.currentStatus}</span>
                </td>
                <td>{t.latestEnvironmentNameSnapshot}</td>
                <td>
                  {t.currentFailureSinceRunId ? (
                    <Link to={`/runs/${t.currentFailureSinceRunId}`}>Run #{t.currentFailureSinceRunId}</Link>
                  ) : (
                    "—"
                  )}
                </td>
                <td>{formatSince(t.failureDurationSeconds)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
