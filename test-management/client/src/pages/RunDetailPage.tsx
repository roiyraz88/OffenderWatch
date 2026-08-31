import { Fragment, useEffect, useRef, useState } from "react";
import { useParams } from "react-router-dom";
import { ApiError } from "../api/client";
import { getRun, stopRun } from "../api/runs";
import { useRunLiveUpdates } from "../hooks/useRunLiveUpdates";
import type { RunDetail, RunSummary, ScenarioResult } from "../types/run";

function formatDuration(seconds: number | null): string {
  if (seconds === null) return "—";
  if (seconds < 60) return `${seconds.toFixed(1)}s`;
  const m = Math.floor(seconds / 60);
  const s = Math.round(seconds % 60);
  return `${m}m ${s}s`;
}

function formatTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : "—";
}

const CONNECTION_LABEL: Record<string, string> = {
  connecting: "Connecting…",
  live: "Live",
  reconnecting: "Reconnecting…",
  disconnected: "Disconnected",
};

// TM-03 — Run Details (Step 5). Hydrates from REST, then stays live via
// SignalR: RunUpdated/ScenarioUpdated apply as incremental in-place updates
// (5.13) so an active run's scenarios visibly transition without the user
// ever pressing Refresh (5.9). Refresh remains as a manual fallback.
export function RunDetailPage() {
  const { id } = useParams<{ id: string }>();
  const runId = Number(id);

  const [run, setRun] = useState<RunDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [stopping, setStopping] = useState(false);
  const [stopError, setStopError] = useState<string | null>(null);

  // Guards against a REST response that started before a newer SignalR
  // event arrived from clobbering it — always keep the freshest state.
  const lastAppliedRef = useRef(0);

  async function load() {
    setLoading(true);
    setLoadError(null);
    const requestedAt = Date.now();
    try {
      const fresh = await getRun(runId);
      if (requestedAt >= lastAppliedRef.current) {
        lastAppliedRef.current = requestedAt;
        setRun(fresh);
      }
    } catch (err) {
      setLoadError(err instanceof ApiError ? err.message : "Could not reach the API.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [runId]);

  const { connectionState } = useRunLiveUpdates({
    runId,
    onNeedsRefetch: load,
    onRunUpdated: (updated: RunSummary) => {
      lastAppliedRef.current = Date.now();
      setRun((current) => (current ? { ...current, ...updated } : current));
    },
    onScenarioUpdated: (scenario: ScenarioResult) => {
      lastAppliedRef.current = Date.now();
      setRun((current) => {
        if (!current) return current;
        const exists = current.scenarioResults.some((sr) => sr.id === scenario.id);
        const scenarioResults = exists
          ? current.scenarioResults.map((sr) => (sr.id === scenario.id ? scenario : sr))
          : [...current.scenarioResults, scenario].sort((a, b) => a.id - b.id);
        return { ...current, scenarioResults };
      });
    },
  });

  async function handleStop() {
    setStopping(true);
    setStopError(null);
    try {
      await stopRun(runId);
      await load();
    } catch (err) {
      setStopError(err instanceof ApiError ? err.message : "Could not stop the run — the API is unreachable.");
    } finally {
      setStopping(false);
    }
  }

  if (loading && !run) {
    return <p>Loading run…</p>;
  }

  if (loadError && !run) {
    return (
      <div className="error-banner">
        <p>{loadError}</p>
        <button onClick={load}>Retry</button>
      </div>
    );
  }

  if (!run) {
    return null;
  }

  const canStop = run.status === "Queued" || run.status === "Running";

  return (
    <section>
      <div className="page-header">
        <h1>Run #{run.id}</h1>
        <div>
          <span className={`connection-indicator connection-${connectionState}`}>
            {CONNECTION_LABEL[connectionState]}
          </span>
          <button onClick={load} disabled={loading}>
            Refresh
          </button>
          {canStop && (
            <button onClick={handleStop} disabled={stopping} style={{ marginLeft: "0.5rem" }}>
              {stopping ? "Stopping…" : "Stop"}
            </button>
          )}
        </div>
      </div>

      {stopError && <div className="error-banner">{stopError}</div>}

      <dl className="run-meta">
        <dt>Environment</dt>
        <dd>
          {run.environmentNameSnapshot} <span className="mono">({run.baseUrlSnapshot})</span>
        </dd>
        <dt>Status</dt>
        <dd>
          <span className={`status-badge status-${run.status.toLowerCase()}`}>{run.status}</span>
        </dd>
        <dt>Trigger</dt>
        <dd>{run.trigger}</dd>
        <dt>Start / End</dt>
        <dd>
          {formatTime(run.startedAtUtc)} → {formatTime(run.endedAtUtc)}
        </dd>
        <dt>Duration</dt>
        <dd>{formatDuration(run.durationSeconds)}</dd>
        <dt>Totals</dt>
        <dd>
          {run.passedCount} passed · {run.failedCount} failed · {run.expectedFailedCount} expected-fail ·{" "}
          {run.skippedCount} skipped
        </dd>
      </dl>

      <h2>Scenarios</h2>
      {run.scenarioResults.length === 0 ? (
        <p>No scenarios recorded yet.</p>
      ) : (
        <table className="env-table">
          <thead>
            <tr>
              <th>Test</th>
              <th>Suite</th>
              <th>Requirement</th>
              <th>Bug</th>
              <th>Status</th>
              <th>Duration</th>
            </tr>
          </thead>
          <tbody>
            {run.scenarioResults.map((sr) => (
              <Fragment key={sr.id}>
                <tr>
                  <td className="mono">{sr.name}</td>
                  <td>{sr.suite}</td>
                  <td>{sr.requirementId ?? "—"}</td>
                  <td>{sr.bugId ?? "—"}</td>
                  <td>
                    <span className={`status-badge status-${sr.status.toLowerCase()}`}>{sr.status}</span>
                  </td>
                  <td>{sr.durationMs !== null ? `${sr.durationMs}ms` : "—"}</td>
                </tr>
                {(sr.status === "Failed" || sr.status === "ExpectedFail") && sr.failureMessage && (
                  <tr className="failure-row">
                    <td colSpan={6}>{sr.failureMessage}</td>
                  </tr>
                )}
              </Fragment>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
