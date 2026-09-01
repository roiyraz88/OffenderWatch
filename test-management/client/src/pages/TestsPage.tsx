import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ApiError } from "../api/client";
import { getTests } from "../api/tests";
import { LoadingSpinner } from "../components/LoadingSpinner";
import { PageLoading } from "../components/PageLoading";
import type { TestCaseSummary } from "../types/test";

function formatTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : "—";
}

// TM-04 — Test history (Step 6). Every row is derived from persisted TestCase + ScenarioResult data.
export function TestsPage() {
  const navigate = useNavigate();
  const [tests, setTests] = useState<TestCaseSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      setTests(await getTests());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not reach the API.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  return (
    <section>
      <div className="page-header">
        <h1>Tests</h1>
      </div>

      {error && (
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
      )}

      {!error && tests === null && <PageLoading label="Loading tests…" />}
      {!error && tests !== null && tests.length === 0 && <p>No tests recorded yet — run the suite from the Runs page first.</p>}

      {!error && tests !== null && tests.length > 0 && (
        <table className="env-table">
          <thead>
            <tr>
              <th>Test</th>
              <th>Suite</th>
              <th>Requirement</th>
              <th>Bug</th>
              <th>Last Status</th>
              <th>Last Run</th>
              <th>Last Execution</th>
              <th>Flaky</th>
            </tr>
          </thead>
          <tbody>
            {tests.map((t) => (
              <tr key={t.id} className="run-row" onClick={() => navigate(`/tests/${t.id}`)}>
                <td className="mono">{t.name}</td>
                <td>{t.suite}</td>
                <td>{t.requirementId ?? "—"}</td>
                <td>{t.bugId ?? "—"}</td>
                <td>
                  {t.lastStatus ? (
                    <span className={`status-badge status-${t.lastStatus.toLowerCase()}`}>{t.lastStatus}</span>
                  ) : (
                    "—"
                  )}
                </td>
                <td>{t.lastRunId ? `#${t.lastRunId}` : "—"}</td>
                <td>{formatTime(t.lastExecutedAtUtc)}</td>
                <td>{t.isFlaky ? <span className="flaky-badge">Flaky</span> : "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
