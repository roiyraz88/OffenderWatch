import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ApiError } from "../api/client";
import { getEnvironments } from "../api/environments";
import { createRun, getRuns } from "../api/runs";
import { LoadingSpinner } from "../components/LoadingSpinner";
import { PageLoading } from "../components/PageLoading";
import type { Environment } from "../types/environment";
import type { RunSummary } from "../types/run";

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

// TM-02 — Run execution & management (Step 4).
export function RunsPage() {
  const navigate = useNavigate();

  const [runs, setRuns] = useState<RunSummary[]>([]);
  const [environments, setEnvironments] = useState<Environment[]>([]);
  const [selectedEnvironmentId, setSelectedEnvironmentId] = useState<number | null>(null);

  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setLoadError(null);
    try {
      const [runsData, environmentsData] = await Promise.all([getRuns(), getEnvironments()]);
      setRuns(runsData);
      setEnvironments(environmentsData);
      setSelectedEnvironmentId((current) => {
        if (current !== null && environmentsData.some((e) => e.id === current)) {
          return current;
        }
        const defaultEnv = environmentsData.find((e) => e.isDefault);
        return defaultEnv ? defaultEnv.id : (environmentsData[0]?.id ?? null);
      });
    } catch (err) {
      setLoadError(err instanceof ApiError ? err.message : "Could not reach the API.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  const isInitialLoad = loading && runs.length === 0 && environments.length === 0 && !loadError;

  async function handleStart() {
    if (selectedEnvironmentId === null) return;
    setStarting(true);
    setStartError(null);
    try {
      const run = await createRun({ environmentId: selectedEnvironmentId });
      navigate(`/runs/${run.id}`);
    } catch (err) {
      setStartError(err instanceof ApiError ? err.message : "Could not start the run — the API is unreachable.");
    } finally {
      setStarting(false);
    }
  }

  if (isInitialLoad) {
    return <PageLoading label="Loading runs…" />;
  }

  return (
    <section>
      <div className="page-header">
        <h1>Runs</h1>
        <Link to="/runs/compare">Compare Runs</Link>
      </div>

      <div className="start-run-row">
        {environments.length === 0 && !loading ? (
          <span>
            No environments configured yet — add one on the <Link to="/environments">Environments</Link> page first.
          </span>
        ) : (
          <>
            <div className="field">
              <label className="field-label" htmlFor="start-run-environment">
                Environment
              </label>
              <select
                id="start-run-environment"
                value={selectedEnvironmentId ?? ""}
                onChange={(e) => setSelectedEnvironmentId(Number(e.target.value))}
                disabled={starting}
              >
                {environments.map((env) => (
                  <option key={env.id} value={env.id}>
                    {env.name}
                    {env.isDefault ? " (default)" : ""}
                  </option>
                ))}
              </select>
            </div>
            <button
              className="btn-primary"
              onClick={handleStart}
              disabled={starting || selectedEnvironmentId === null}
            >
              {starting ? (
                <>
                  <LoadingSpinner size="sm" announce={false} />
                  Starting…
                </>
              ) : (
                "Start New Run"
              )}
            </button>
          </>
        )}
      </div>

      {startError && <div className="error-banner">{startError}</div>}

      {loadError && (
        <div className="error-banner">
          <p>{loadError}</p>
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
      )}

      {!loading && !loadError && runs.length === 0 && <p>No runs yet — start one above.</p>}

      {!loading && !loadError && runs.length > 0 && (
        <table className="env-table">
          <thead>
            <tr>
              <th>Id</th>
              <th>Environment</th>
              <th>Status</th>
              <th>Trigger</th>
              <th>Start</th>
              <th>End</th>
              <th>Duration</th>
              <th>Passed</th>
              <th>Failed</th>
              <th>Expected Fail</th>
              <th>Skipped</th>
            </tr>
          </thead>
          <tbody>
            {runs.map((run) => (
              <tr key={run.id} className="run-row" onClick={() => navigate(`/runs/${run.id}`)}>
                <td>
                  <Link to={`/runs/${run.id}`} onClick={(e) => e.stopPropagation()}>
                    #{run.id}
                  </Link>
                </td>
                <td>{run.environmentNameSnapshot}</td>
                <td>
                  <span className={`status-badge status-${run.status.toLowerCase()}`}>{run.status}</span>
                </td>
                <td>{run.trigger}</td>
                <td>{formatTime(run.startedAtUtc)}</td>
                <td>{formatTime(run.endedAtUtc)}</td>
                <td>{formatDuration(run.durationSeconds)}</td>
                <td>{run.passedCount}</td>
                <td>{run.failedCount}</td>
                <td>{run.expectedFailedCount}</td>
                <td>{run.skippedCount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
