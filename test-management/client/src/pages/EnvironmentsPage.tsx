import { useEffect, useState } from "react";
import {
  createEnvironment,
  deleteEnvironment,
  getEnvironments,
  setDefaultEnvironment,
  updateEnvironment,
} from "../api/environments";
import { ApiError } from "../api/client";
import { EnvironmentFormModal, type EnvironmentFormValues } from "../components/EnvironmentFormModal";
import { ConfirmDeleteModal } from "../components/ConfirmDeleteModal";
import { LoadingSpinner } from "../components/LoadingSpinner";
import { PageLoading } from "../components/PageLoading";
import type { Environment } from "../types/environment";

type LoadState = "loading" | "loaded" | "error";

// TM-01 — Environment configuration, implemented in full (Step 3).
export function EnvironmentsPage() {
  const [environments, setEnvironments] = useState<Environment[]>([]);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [loadError, setLoadError] = useState<string | null>(null);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Environment | undefined>(undefined);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | undefined>(undefined);

  const [actionError, setActionError] = useState<string | null>(null);
  const [pendingAction, setPendingAction] = useState<{ id: number; kind: "setDefault" } | null>(null);

  const [deleteTarget, setDeleteTarget] = useState<Environment | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  async function load() {
    setLoadState("loading");
    setLoadError(null);
    try {
      const data = await getEnvironments();
      setEnvironments(data);
      setLoadState("loaded");
    } catch (err) {
      setLoadError(err instanceof ApiError ? err.message : "Could not reach the API.");
      setLoadState("error");
    }
  }

  useEffect(() => {
    load();
  }, []);

  function openCreate() {
    setEditing(undefined);
    setFormError(undefined);
    setModalOpen(true);
  }

  function openEdit(env: Environment) {
    setEditing(env);
    setFormError(undefined);
    setModalOpen(true);
  }

  async function handleFormSubmit(values: EnvironmentFormValues) {
    setSubmitting(true);
    setFormError(undefined);
    try {
      if (editing) {
        await updateEnvironment(editing.id, { name: values.name, baseUrl: values.baseUrl });
      } else {
        await createEnvironment(values);
      }
      setModalOpen(false);
      await load();
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : "Save failed — the API is unreachable.");
    } finally {
      setSubmitting(false);
    }
  }

  function openDeleteConfirm(env: Environment) {
    setDeleteTarget(env);
    setDeleteError(null);
  }

  function closeDeleteConfirm() {
    if (deleting) return; // guarded by the modal's own disabled buttons/Escape too — belt and suspenders
    setDeleteTarget(null);
    setDeleteError(null);
  }

  async function handleConfirmDelete() {
    if (!deleteTarget || deleting) {
      return; // also guards against a duplicate request firing twice
    }
    setDeleting(true);
    setDeleteError(null);
    try {
      await deleteEnvironment(deleteTarget.id);
      setDeleteTarget(null);
      await load();
    } catch (err) {
      setDeleteError(err instanceof ApiError ? err.message : "Delete failed — the API is unreachable.");
    } finally {
      setDeleting(false);
    }
  }

  async function handleSetDefault(env: Environment) {
    setActionError(null);
    setPendingAction({ id: env.id, kind: "setDefault" });
    try {
      await setDefaultEnvironment(env.id);
      await load();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Could not set default — the API is unreachable.");
    } finally {
      setPendingAction(null);
    }
  }

  // A full-page spinner only makes sense while there's nothing to preserve
  // underneath it — the true initial load, or a retry after a failure that
  // never had any data. A background reload after create/edit/delete/
  // set-default (loadState briefly "loading" again with environments already
  // populated) must leave the table exactly where it is.
  const isLoadingNow = loadState === "loading";
  const showFullPageSpinner = isLoadingNow && environments.length === 0;

  return (
    <section>
      <div className="page-header">
        <h1>Environments</h1>
        <button onClick={openCreate}>Add environment</button>
      </div>

      {showFullPageSpinner && <PageLoading label="Loading environments…" />}

      {loadState === "error" && (
        <div className="error-banner">
          <p>{loadError}</p>
          <button onClick={load} disabled={isLoadingNow}>
            {isLoadingNow ? (
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

      {actionError && <div className="error-banner">{actionError}</div>}

      {!showFullPageSpinner && loadState !== "error" && environments.length === 0 && (
        <p>No environments yet. Add one to start recording runs against it.</p>
      )}

      {!showFullPageSpinner && loadState !== "error" && environments.length > 0 && (
        <table className="env-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Base URL</th>
              <th>Default</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {environments.map((env) => (
              <tr key={env.id}>
                <td>{env.name}</td>
                <td className="mono">{env.baseUrl}</td>
                <td>{env.isDefault ? <span className="default-badge">Default</span> : null}</td>
                <td className="actions">
                  <button onClick={() => openEdit(env)} disabled={pendingAction?.id === env.id}>
                    Edit
                  </button>
                  <button onClick={() => openDeleteConfirm(env)} disabled={pendingAction?.id === env.id}>
                    Delete
                  </button>
                  {!env.isDefault && (
                    <button onClick={() => handleSetDefault(env)} disabled={pendingAction?.id === env.id}>
                      {pendingAction?.id === env.id && pendingAction.kind === "setDefault" ? (
                        <>
                          <LoadingSpinner size="sm" announce={false} />
                          Setting…
                        </>
                      ) : (
                        "Set default"
                      )}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {modalOpen && (
        <EnvironmentFormModal
          editing={editing}
          submitting={submitting}
          errorMessage={formError}
          onSubmit={handleFormSubmit}
          onCancel={() => setModalOpen(false)}
        />
      )}

      {deleteTarget && (
        <ConfirmDeleteModal
          title="Delete environment?"
          message={`Are you sure you want to delete "${deleteTarget.name}"?`}
          warning="Historical runs will not be deleted."
          isDeleting={deleting}
          errorMessage={deleteError}
          onConfirm={handleConfirmDelete}
          onCancel={closeDeleteConfirm}
        />
      )}
    </section>
  );
}
