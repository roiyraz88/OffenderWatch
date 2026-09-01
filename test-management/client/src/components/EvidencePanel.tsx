import { useEffect, useRef, useState } from "react";
import { ApiError } from "../api/client";
import { evidenceContentUrl, getScenarioEvidence } from "../api/evidence";
import { LoadingSpinner } from "./LoadingSpinner";
import { PageLoading } from "./PageLoading";
import { stripAnsi } from "../utils/stripAnsi";
import type { EvidenceArtifact } from "../types/evidence";

interface EvidencePanelProps {
  runId: number;
  scenarioResultId: number;
  scenarioName: string;
  failureMessage?: string | null;
  stackTrace?: string | null;
  onClose: () => void;
}

/** Pretty-prints valid JSON for display only — never touches what's actually stored. Falls back to the raw (ANSI-stripped) text for anything that isn't valid JSON. */
function formatIfJson(text: string): string {
  const clean = stripAnsi(text);
  try {
    return JSON.stringify(JSON.parse(clean), null, 2);
  } catch {
    return clean;
  }
}

function sanitizeFilenamePart(input: string): string {
  const cleaned = input
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 60);
  return cleaned || "scenario";
}

function extensionForContentType(contentType: string): string {
  if (contentType.includes("png")) return "png";
  if (contentType.includes("jpeg") || contentType.includes("jpg")) return "jpg";
  if (contentType.includes("gif")) return "gif";
  if (contentType.includes("webp")) return "webp";
  return "png";
}

/** TM-08 (6.19) — a simple panel, not a full report viewer, for one ScenarioResult's evidence. */
export function EvidencePanel({
  runId,
  scenarioResultId,
  scenarioName,
  failureMessage,
  stackTrace,
  onClose,
}: EvidencePanelProps) {
  const [artifacts, setArtifacts] = useState<EvidenceArtifact[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [jsonBodies, setJsonBodies] = useState<Record<number, string>>({});
  const [logBodies, setLogBodies] = useState<Record<number, string>>({});
  const [screenshotError, setScreenshotError] = useState<string | null>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    closeButtonRef.current?.focus();
  }, []);

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = await getScenarioEvidence(runId, scenarioResultId);
        if (cancelled) return;
        setArtifacts(list);

        for (const artifact of list) {
          if (artifact.type === "ApiRequest" || artifact.type === "ApiResponse") {
            fetch(evidenceContentUrl(artifact.id))
              .then((r) => r.text())
              .then((text) => {
                if (!cancelled) setJsonBodies((prev) => ({ ...prev, [artifact.id]: formatIfJson(text) }));
              })
              .catch(() => {});
          } else if (artifact.type === "Log") {
            fetch(evidenceContentUrl(artifact.id))
              .then((r) => r.text())
              .then((text) => {
                // Display only — the fetched text itself is never persisted
                // anywhere; this state exists purely to render it.
                if (!cancelled) setLogBodies((prev) => ({ ...prev, [artifact.id]: stripAnsi(text) }));
              })
              .catch(() => {});
          }
        }
      } catch (err) {
        if (!cancelled) setError(err instanceof ApiError ? err.message : "Could not load evidence.");
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [runId, scenarioResultId]);

  async function handleOpenFullSize(artifact: EvidenceArtifact) {
    setScreenshotError(null);
    try {
      window.open(evidenceContentUrl(artifact.id), "_blank", "noopener,noreferrer");
    } catch {
      setScreenshotError("Could not open the screenshot in a new tab.");
    }
  }

  async function handleDownload(artifact: EvidenceArtifact) {
    setScreenshotError(null);
    try {
      // Fetched as a blob (the exact original bytes from the existing
      // evidence content endpoint — no resizing/re-rendering) so the
      // filename we choose is honored regardless of origin, then handed to
      // the browser via a throwaway object URL. Reuses GET
      // /api/evidence/{id}/content — no backend change.
      const response = await fetch(evidenceContentUrl(artifact.id));
      if (!response.ok) {
        throw new Error(`Download failed: ${response.status}`);
      }
      const blob = await response.blob();
      const objectUrl = URL.createObjectURL(blob);
      const filename = `run-${runId}-${sanitizeFilenamePart(scenarioName)}-screenshot.${extensionForContentType(artifact.contentType)}`;
      const link = document.createElement("a");
      link.href = objectUrl;
      link.download = filename;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(objectUrl);
    } catch {
      setScreenshotError("Could not download the screenshot — the API may be unreachable.");
    }
  }

  const logArtifact = artifacts?.find((a) => a.type === "Log");
  const requestArtifact = artifacts?.find((a) => a.type === "ApiRequest");
  const responseArtifact = artifacts?.find((a) => a.type === "ApiResponse");
  const screenshotArtifact = artifacts?.find((a) => a.type === "Screenshot");
  const traceArtifact = artifacts?.find((a) => a.type === "Trace");
  const hasFailureDetails = Boolean(failureMessage || stackTrace);

  return (
    <div className="evidence-backdrop" onClick={onClose}>
      <div
        className="evidence-panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="evidence-panel-title"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="evidence-panel-header">
          <h3 id="evidence-panel-title">Evidence — {scenarioName}</h3>
          <button ref={closeButtonRef} onClick={onClose}>
            Close
          </button>
        </div>

        <div className="evidence-panel-body">
          {error && <div className="error-banner">{error}</div>}
          {!error && artifacts === null && <PageLoading label="Loading evidence…" />}
          {!error && artifacts !== null && artifacts.length === 0 && !hasFailureDetails && (
            <p>No evidence recorded for this scenario.</p>
          )}

          {hasFailureDetails && (
            <section className="evidence-section">
              <h4 className="evidence-section-title">Failure Details</h4>
              {failureMessage && <p className="evidence-failure-message">{stripAnsi(failureMessage)}</p>}
              {stackTrace && <pre className="evidence-text evidence-text-error">{stripAnsi(stackTrace)}</pre>}
            </section>
          )}

          {logArtifact && (
            <section className="evidence-section">
              <h4 className="evidence-section-title">
                Execution Log <span className="mono evidence-meta">({logArtifact.contentType}, {logArtifact.sizeBytes} bytes)</span>
              </h4>
              <pre className="evidence-text">
                {logBodies[logArtifact.id] ?? (
                  <span className="evidence-text-loading">
                    <LoadingSpinner size="sm" announce={false} />
                    Loading…
                  </span>
                )}
              </pre>
            </section>
          )}

          {requestArtifact && (
            <section className="evidence-section">
              <h4 className="evidence-section-title">Request</h4>
              <pre className="evidence-text">
                {jsonBodies[requestArtifact.id] ?? (
                  <span className="evidence-text-loading">
                    <LoadingSpinner size="sm" announce={false} />
                    Loading…
                  </span>
                )}
              </pre>
            </section>
          )}

          {responseArtifact && (
            <section className="evidence-section">
              <h4 className="evidence-section-title">Response</h4>
              <pre className="evidence-text">
                {jsonBodies[responseArtifact.id] ?? (
                  <span className="evidence-text-loading">
                    <LoadingSpinner size="sm" announce={false} />
                    Loading…
                  </span>
                )}
              </pre>
            </section>
          )}

          {screenshotArtifact && (
            <section className="evidence-section">
              <h4 className="evidence-section-title">Screenshot</h4>
              <img
                src={evidenceContentUrl(screenshotArtifact.id)}
                alt={`Screenshot evidence for ${scenarioName}`}
                className="evidence-screenshot"
              />
              <div className="evidence-screenshot-actions">
                <button onClick={() => handleOpenFullSize(screenshotArtifact)}>Open full size</button>
                <button onClick={() => handleDownload(screenshotArtifact)}>Download screenshot</button>
              </div>
              {screenshotError && <div className="error-banner">{screenshotError}</div>}
            </section>
          )}

          {traceArtifact && (
            <section className="evidence-section">
              <h4 className="evidence-section-title">Trace</h4>
              <a href={evidenceContentUrl(traceArtifact.id)} download className="evidence-download">
                Download trace.zip
              </a>
            </section>
          )}
        </div>
      </div>
    </div>
  );
}
