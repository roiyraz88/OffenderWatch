import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ApiError } from "../api/client";
import { cleanTestDataBatch, cleanTestDataRecord, getTestData } from "../api/testData";
import type { TestDataRecord } from "../types/testData";

function formatTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : "—";
}

// TM-06 — Test Data Lifecycle (Step 7). Every row is an explicitly-owned
// TestDataRecord; cleanup always goes through the backend, which resolves
// the real target itself — this page never sends a target id or BaseUrl.
export function TestDataPage() {
  const [records, setRecords] = useState<TestDataRecord[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [cleaningIds, setCleaningIds] = useState<Set<number>>(new Set());
  const [cleaningAll, setCleaningAll] = useState(false);

  async function load() {
    setError(null);
    try {
      setRecords(await getTestData());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not reach the API.");
    }
  }

  useEffect(() => {
    load();
  }, []);

  async function handleClean(record: TestDataRecord) {
    const label = record.identifier ?? record.externalId ?? `#${record.id}`;
    if (!window.confirm(`Clean ${record.entityType} '${label}' from the target application?`)) {
      return;
    }
    setActionError(null);
    setCleaningIds((prev) => new Set(prev).add(record.id));
    try {
      const updated = await cleanTestDataRecord(record.id);
      // Always the real backend response — never optimistically marked Cleaned.
      setRecords((prev) => prev?.map((r) => (r.id === updated.id ? updated : r)) ?? prev);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Could not clean this record — the API is unreachable.");
    } finally {
      setCleaningIds((prev) => {
        const next = new Set(prev);
        next.delete(record.id);
        return next;
      });
    }
  }

  async function handleCleanAllActive() {
    const active = (records ?? []).filter((r) => r.cleanupStatus === "Active");
    if (active.length === 0) return;
    if (!window.confirm(`Clean all ${active.length} Active record(s) from the target application?`)) {
      return;
    }
    setActionError(null);
    setCleaningAll(true);
    try {
      const updated = await cleanTestDataBatch(active.map((r) => r.id));
      const byId = new Map(updated.map((r) => [r.id, r]));
      setRecords((prev) => prev?.map((r) => byId.get(r.id) ?? r) ?? prev);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Could not clean these records — the API is unreachable.");
    } finally {
      setCleaningAll(false);
    }
  }

  const activeCount = (records ?? []).filter((r) => r.cleanupStatus === "Active").length;

  return (
    <section>
      <div className="page-header">
        <h1>Test data</h1>
        {activeCount > 0 && (
          <button onClick={handleCleanAllActive} disabled={cleaningAll}>
            {cleaningAll ? "Cleaning…" : `Clean All Active (${activeCount})`}
          </button>
        )}
      </div>

      {actionError && <div className="error-banner">{actionError}</div>}

      {error && (
        <div className="error-banner">
          <p>{error}</p>
          <button onClick={load}>Retry</button>
        </div>
      )}

      {!error && records === null && <p>Loading test data…</p>}
      {!error && records !== null && records.length === 0 && (
        <p>No test-created data tracked yet — it appears here automatically as automation runs create it.</p>
      )}

      {!error && records !== null && records.length > 0 && (
        <table className="env-table">
          <thead>
            <tr>
              <th>Entity Type</th>
              <th>Identifier</th>
              <th>Run</th>
              <th>Environment</th>
              <th>Created</th>
              <th>Status</th>
              <th>Cleaned At</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {records.map((r) => (
              <tr key={r.id}>
                <td>{r.entityType}</td>
                <td className="mono">{r.identifier ?? r.externalId ?? "—"}</td>
                <td>
                  <Link to={`/runs/${r.testRunId}`}>Run #{r.testRunId}</Link>
                </td>
                <td>{r.environmentNameSnapshot}</td>
                <td>{formatTime(r.createdAtUtc)}</td>
                <td>
                  <span className={`status-badge status-${r.cleanupStatus.toLowerCase()}`}>{r.cleanupStatus}</span>
                </td>
                <td>{formatTime(r.cleanedAtUtc)}</td>
                <td>
                  {r.cleanupStatus !== "Cleaned" && (
                    <button onClick={() => handleClean(r)} disabled={cleaningIds.has(r.id)}>
                      {cleaningIds.has(r.id) ? "Cleaning…" : r.cleanupStatus === "CleanupFailed" ? "Retry Clean" : "Clean"}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
