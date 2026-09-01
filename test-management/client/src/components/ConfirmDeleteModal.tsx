import { useEffect, useRef } from "react";
import { LoadingSpinner } from "./LoadingSpinner";

interface ConfirmDeleteModalProps {
  title: string;
  message: string;
  warning?: string;
  confirmLabel?: string;
  pendingLabel?: string;
  isDeleting: boolean;
  errorMessage?: string | null;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * A small reusable destructive-confirmation modal — replaces window.confirm
 * anywhere in the app that needs one. Cancel is focused by default (the
 * safer choice for a destructive action); Escape and a backdrop click both
 * cancel, but only while nothing is in flight (same as the buttons).
 */
export function ConfirmDeleteModal({
  title,
  message,
  warning,
  confirmLabel = "Delete",
  pendingLabel = "Deleting…",
  isDeleting,
  errorMessage,
  onConfirm,
  onCancel,
}: ConfirmDeleteModalProps) {
  const cancelRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    cancelRef.current?.focus();
  }, []);

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape" && !isDeleting) {
        onCancel();
      }
    }
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isDeleting, onCancel]);

  return (
    <div
      className="modal-backdrop"
      onClick={() => {
        if (!isDeleting) onCancel();
      }}
    >
      <div
        className="confirm-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-delete-title"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 id="confirm-delete-title">{title}</h2>
        <p>{message}</p>
        {warning && <p className="confirm-modal-warning">{warning}</p>}

        {errorMessage && <p className="form-error">{errorMessage}</p>}

        <div className="modal-actions">
          <button ref={cancelRef} type="button" onClick={onCancel} disabled={isDeleting}>
            Cancel
          </button>
          <button type="button" className="btn-danger" onClick={onConfirm} disabled={isDeleting}>
            {isDeleting ? (
              <>
                <LoadingSpinner size="sm" announce={false} />
                {pendingLabel}
              </>
            ) : (
              confirmLabel
            )}
          </button>
        </div>
      </div>
    </div>
  );
}
