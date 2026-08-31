import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { ApiError } from "../api/client";
import { getTestHistory } from "../api/tests";
import type { TestCaseDetail } from "../types/test";

function formatTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : "—";
}

// TM-04 — Test Details / history (Step 6).
export function TestDetailPage() {
  const { id } = useParams<{ id: string }>();
  const testCaseId = Number(id);

  const [test, setTest] = useState<TestCaseDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setError(null);
    try {
      setTest(await getTestHistory(testCaseId));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not reach the API.");
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [testCaseId]);

  if (error) {
    return (
      <div className="error-banner">
        <p>{error}</p>
        <button onClick={load}>Retry</button>
      </div>
    );
  }

  if (!test) {
    return <p>Loading test…</p>;
  }

  return (
    <section>
      <div className="page-header">
        <h1>{test.name}</h1>
      </div>

      <dl className="run-meta">
        <dt>External Id</dt>
        <dd className="mono">{test.externalId}</dd>
        <dt>Suite</dt>
        <dd>{test.suite}</dd>
        <dt>Requirement</dt>
        <dd>{test.requirementId ?? "—"}</dd>
        <dt>Bug</dt>
        <dd>{test.bugId ?? "—"}</dd>
        <dt>Last Status</dt>
        <dd>
          {test.lastStatus ? (
            <span className={`status-badge status-${test.lastStatus.toLowerCase()}`}>{test.lastStatus}</span>
          ) : (
            "—"
          )}
        </dd>
        <dt>Last Pass</dt>
        <dd>
          {test.lastPassRunId ? (
            <>
              <Link to={`/runs/${test.lastPassRunId}`}>Run #{test.lastPassRunId}</Link> — {formatTime(test.lastPassAtUtc)}
            </>
          ) : (
            "Never passed"
          )}
        </dd>
        <dt>Currently Failing Since</dt>
        <dd>
          {test.currentFailureSinceRunId ? (
            <>
              <Link to={`/runs/${test.currentFailureSinceRunId}`}>Run #{test.currentFailureSinceRunId}</Link> —{" "}
              {formatTime(test.currentFailureSinceUtc)}
            </>
          ) : (
            "Not currently failing"
          )}
        </dd>
        <dt>Flaky</dt>
        <dd>{test.isFlaky ? <span className="flaky-badge">Flaky</span> : "No"}</dd>
      </dl>

      <h2>Execution History</h2>
      {test.history.length === 0 ? (
        <p>No executions recorded yet.</p>
      ) : (
        <table className="env-table">
          <thead>
            <tr>
              <th>Run</th>
              <th>Environment</th>
              <th>Date</th>
              <th>Status</th>
              <th>Duration</th>
              <th>Transition</th>
            </tr>
          </thead>
          <tbody>
            {[...test.history].reverse().map((h) => (
              <tr key={h.scenarioResultId}>
                <td>
                  <Link to={`/runs/${h.runId}`}>Run #{h.runId}</Link>
                </td>
                <td>{h.environmentNameSnapshot}</td>
                <td>{formatTime(h.runStartedAtUtc)}</td>
                <td>
                  <span className={`status-badge status-${h.status.toLowerCase()}`}>{h.status}</span>
                </td>
                <td>{h.durationMs !== null ? `${h.durationMs}ms` : "—"}</td>
                <td>
                  <span className={`transition-badge transition-${h.transition.toLowerCase()}`}>{h.transition}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
