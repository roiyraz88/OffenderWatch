import { useState, type FormEvent } from "react";
import type { Environment } from "../types/environment";

export interface EnvironmentFormValues {
  name: string;
  baseUrl: string;
  isDefault: boolean;
}

interface EnvironmentFormModalProps {
  /** Undefined = creating a new environment; provided = editing this one. */
  editing?: Environment;
  submitting: boolean;
  /** Server-side validation/conflict message from the last failed submit, if any. */
  errorMessage?: string;
  onSubmit: (values: EnvironmentFormValues) => void;
  onCancel: () => void;
}

/**
 * Add/Edit form for TM-01 (Step 3.7). Frontend validation here is only a
 * convenience — the same Name/BaseUrl rules are re-checked authoritatively
 * by EnvironmentService, and its rejection message is surfaced via
 * `errorMessage`.
 */
export function EnvironmentFormModal({
  editing,
  submitting,
  errorMessage,
  onSubmit,
  onCancel,
}: EnvironmentFormModalProps) {
  const [name, setName] = useState(editing?.name ?? "");
  const [baseUrl, setBaseUrl] = useState(editing?.baseUrl ?? "");
  const [isDefault, setIsDefault] = useState(false);
  const [clientError, setClientError] = useState<string | null>(null);

  function handleSubmit(e: FormEvent) {
    e.preventDefault();

    const trimmedName = name.trim();
    const trimmedUrl = baseUrl.trim();

    if (!trimmedName) {
      setClientError("Name is required.");
      return;
    }
    if (!/^https?:\/\/.+/i.test(trimmedUrl)) {
      setClientError("Base URL must start with http:// or https://.");
      return;
    }

    setClientError(null);
    onSubmit({ name: trimmedName, baseUrl: trimmedUrl, isDefault });
  }

  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true">
      <form className="modal" onSubmit={handleSubmit}>
        <h2>{editing ? `Edit ${editing.name}` : "Add environment"}</h2>

        <label>
          Name
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Staging"
            autoFocus
          />
        </label>

        <label>
          Base URL
          <input
            value={baseUrl}
            onChange={(e) => setBaseUrl(e.target.value)}
            placeholder="https://svcdemoaz.puremonitor.supercom.com/AQApplication/..."
          />
        </label>

        {!editing && (
          <label className="checkbox-row">
            <input type="checkbox" checked={isDefault} onChange={(e) => setIsDefault(e.target.checked)} />
            Make default
          </label>
        )}

        {(clientError || errorMessage) && <p className="form-error">{clientError ?? errorMessage}</p>}

        <div className="modal-actions">
          <button type="button" onClick={onCancel} disabled={submitting}>
            Cancel
          </button>
          <button type="submit" disabled={submitting}>
            {submitting ? "Saving…" : "Save"}
          </button>
        </div>
      </form>
    </div>
  );
}
