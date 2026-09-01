import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ApiError } from "../api/client";
import { cleanTestDataBatch, cleanTestDataRecord, getTestData } from "../api/testData";
import { ConfirmDeleteModal } from "../components/ConfirmDeleteModal";
import { LoadingSpinner } from "../components/LoadingSpinner";
import { PageLoading } from "../components/PageLoading";
import type { TestDataRecord } from "../types/testData";

function formatTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : "—";
}

interface BatchSummary {
  cleaned: number;
  failed: number;
}

// TM-06 — Test Data Lifecycle (Step 7). Every row is an explicitly-owned
// TestDataRecord; cleanup always goes through the backend, which resolves
// the real target itself — this page never sends a target id or BaseUrl.
export function TestDataPage() {
  const [records, setRecords] = useState<TestDataRecord[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [cleaningIds, setCleaningIds] = useState<Set<number>>(new Set());
  const [cleaningAll, setCleaningAll] = useState(false);
  const [batchSummary, setBatchSummary] = useState<BatchSummary | null>(null);

  // Confirmation modal state — replaces window.confirm entirely (7.5).
  const [cleanTarget, setCleanTarget] = useState<TestDataRecord | null>(null);
  const [cleanTargetError, setCleanTargetError] = useState<string | null>(null);
  const [showCleanAllConfirm, setShowCleanAllConfirm] = useState(false);
  const [cleanAllError, setCleanAllError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      setRecords(await getTestData());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not reach the API.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  function openCleanConfirm(record: TestDataRecord) {
    setCleanTargetError(null);
    setCleanTarget(record);
  }

  function closeCleanConfirm() {
    if (cleaningIds.size > 0 && cleanTarget && cleaningIds.has(cleanTarget.id)) return;
    setCleanTarget(null);
    setCleanTargetError(null);
  }

  async function handleConfirmClean() {
    if (!cleanTarget) return;
    const record = cleanTarget;
    setCleanTargetError(null);
    setCleaningIds((prev) => new Set(prev).add(record.id));
    try {
      const updated = await cleanTestDataRecord(record.id);
      // Always the real backend response — never optimistically marked Cleaned.
      setRecords((prev) => prev?.map((r) => (r.id === updated.id ? updated : r)) ?? prev);
      setCleanTarget(null);
    } catch (err) {
      setCleanTargetError(
        err instanceof ApiError ? err.message : "Could not clean this record — the API is unreachable.",
      );
    } finally {
      setCleaningIds((prev) => {
        const next = new Set(prev);
        next.delete(record.id);
        return next;
      });
    }
  }

  const activeRecords = (records ?? []).filter((r) => r.cleanupStatus === "Active");
  const activeEnvironments = new Set(activeRecords.map((r) => r.environmentNameSnapshot));

  function openCleanAllConfirm() {
    if (activeRecords.length === 0) return;
    setCleanAllError(null);
    setBatchSummary(null);
    setShowCleanAllConfirm(true);
  }

  function closeCleanAllConfirm() {
    if (cleaningAll) return;
    setShowCleanAllConfirm(false);
    setCleanAllError(null);
  }

  async function handleConfirmCleanAll() {
    setCleanAllError(null);
    setCleaningAll(true);
    try {
      const updated = await cleanTestDataBatch(activeRecords.map((r) => r.id));
      const byId = new Map(updated.map((r) => [r.id, r]));
      setRecords((prev) => prev?.map((r) => byId.get(r.id) ?? r) ?? prev);
      setBatchSummary({
        cleaned: updated.filter((r) => r.cleanupStatus === "Cleaned").length,
        failed: updated.filter((r) => r.cleanupStatus === "CleanupFailed").length,
      });
      setShowCleanAllConfirm(false);
    } catch (err) {
      setCleanAllError(err instanceof ApiError ? err.message : "Could not clean these records — the API is unreachable.");
    } finally {
      setCleaningAll(false);
    }
  }

  const activeCount = activeRecords.length;

  const cleanTargetIsUnsupportedLocationPoint = cleanTarget?.entityType === "LocationPoint";

  return (
    <section>
      <div className="page-header">
        <h1>Test data</h1>
        {activeCount > 0 && (
          <button onClick={openCleanAllConfirm} disabled={cleaningAll}>
            {cleaningAll ? (
              <>
                <LoadingSpinner size="sm" announce={false} />
                Cleaning…
              </>
            ) : (
              `Clean All Active (${activeCount})`
            )}
          </button>
        )}
      </div>

      {batchSummary && (
        <div className="info-banner">
          Cleaned: {batchSummary.cleaned} · Failed/Unsupported: {batchSummary.failed}
        </div>
      )}

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

      {!error && records === null && <PageLoading label="Loading test data…" />}
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
                <td>{r.cleanupStatus === "Cleaned" ? formatTime(r.cleanedAtUtc) : "—"}</td>
                <td>
                  {r.cleanupStatus !== "Cleaned" && (
                    <button onClick={() => openCleanConfirm(r)} disabled={cleaningIds.has(r.id)}>
                      {cleaningIds.has(r.id) ? (
                        <>
                          <LoadingSpinner size="sm" announce={false} />
                          Cleaning…
                        </>
                      ) : r.cleanupStatus === "CleanupFailed" ? (
                        "Retry Clean"
                      ) : (
                        "Clean"
                      )}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {cleanTarget && (
        <ConfirmDeleteModal
          title="Clean test data?"
          message={
            `Entity Type: ${cleanTarget.entityType}\n` +
            `Identifier: ${cleanTarget.identifier ?? cleanTarget.externalId ?? "—"}\n` +
            `Environment: ${cleanTarget.environmentNameSnapshot}\n` +
            `Run: #${cleanTarget.testRunId}\n\n` +
            "This will attempt to remove this automation-created test data from the target environment."
          }
          warning={
            cleanTargetIsUnsupportedLocationPoint
              ? "Individual LocationPoint cleanup is not supported by the target API — this action is expected to be reported as unsupported, not guaranteed to succeed."
              : undefined
          }
          confirmLabel="Clean"
          pendingLabel="Cleaning…"
          isDeleting={cleaningIds.has(cleanTarget.id)}
          errorMessage={cleanTargetError}
          onConfirm={handleConfirmClean}
          onCancel={closeCleanConfirm}
        />
      )}

      {showCleanAllConfirm && (
        <ConfirmDeleteModal
          title="Clean all active test data?"
          message={
            "This will attempt to clean all eligible automation-created test data currently marked Active. " +
            "Safety rules will still be applied and unsupported records may not be cleaned.\n\n" +
            `Active records: ${activeCount}`
          }
          warning={
            activeEnvironments.size > 1
              ? `These records span ${activeEnvironments.size} different environments — cleanup will target each record's own owning environment.`
              : undefined
          }
          confirmLabel="Clean All Active"
          pendingLabel="Cleaning…"
          isDeleting={cleaningAll}
          errorMessage={cleanAllError}
          onConfirm={handleConfirmCleanAll}
          onCancel={closeCleanAllConfirm}
        />
      )}
    </section>
  );
}
