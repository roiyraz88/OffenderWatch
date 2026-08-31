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
  const [pendingActionId, setPendingActionId] = useState<number | null>(null);

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

  async function handleDelete(env: Environment) {
    if (!window.confirm(`Delete environment '${env.name}'?`)) {
      return;
    }
    setActionError(null);
    setPendingActionId(env.id);
    try {
      await deleteEnvironment(env.id);
      await load();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Delete failed — the API is unreachable.");
    } finally {
      setPendingActionId(null);
    }
  }

  async function handleSetDefault(env: Environment) {
    setActionError(null);
    setPendingActionId(env.id);
    try {
      await setDefaultEnvironment(env.id);
      await load();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Could not set default — the API is unreachable.");
    } finally {
      setPendingActionId(null);
    }
  }

  return (
    <section>
      <div className="page-header">
        <h1>Environments</h1>
        <button onClick={openCreate}>Add environment</button>
      </div>

      {loadState === "loading" && <p>Loading environments…</p>}

      {loadState === "error" && (
        <div className="error-banner">
          <p>{loadError}</p>
          <button onClick={load}>Retry</button>
        </div>
      )}

      {actionError && <div className="error-banner">{actionError}</div>}

      {loadState === "loaded" && environments.length === 0 && (
        <p>No environments yet. Add one to start recording runs against it.</p>
      )}

      {loadState === "loaded" && environments.length > 0 && (
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
                  <button onClick={() => openEdit(env)} disabled={pendingActionId === env.id}>
                    Edit
                  </button>
                  <button onClick={() => handleDelete(env)} disabled={pendingActionId === env.id}>
                    Delete
                  </button>
                  {!env.isDefault && (
                    <button onClick={() => handleSetDefault(env)} disabled={pendingActionId === env.id}>
                      Set default
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
    </section>
  );
}
