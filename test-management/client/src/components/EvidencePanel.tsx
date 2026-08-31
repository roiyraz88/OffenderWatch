import { useEffect, useState } from "react";
import { ApiError } from "../api/client";
import { evidenceContentUrl, getScenarioEvidence } from "../api/evidence";
import type { EvidenceArtifact } from "../types/evidence";

interface EvidencePanelProps {
  runId: number;
  scenarioResultId: number;
  scenarioName: string;
  onClose: () => void;
}

/** TM-08 (6.19) — a simple panel, not a full report viewer, for one ScenarioResult's evidence. */
export function EvidencePanel({ runId, scenarioResultId, scenarioName, onClose }: EvidencePanelProps) {
  const [artifacts, setArtifacts] = useState<EvidenceArtifact[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [jsonBodies, setJsonBodies] = useState<Record<number, string>>({});
  const [logBodies, setLogBodies] = useState<Record<number, string>>({});

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
                if (!cancelled) setJsonBodies((prev) => ({ ...prev, [artifact.id]: text }));
              })
              .catch(() => {});
          } else if (artifact.type === "Log") {
            fetch(evidenceContentUrl(artifact.id))
              .then((r) => r.text())
              .then((text) => {
                if (!cancelled) setLogBodies((prev) => ({ ...prev, [artifact.id]: text }));
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

  return (
    <div className="evidence-backdrop" onClick={onClose}>
      <div className="evidence-panel" onClick={(e) => e.stopPropagation()}>
        <div className="evidence-panel-header">
          <h3>Evidence — {scenarioName}</h3>
          <button onClick={onClose}>Close</button>
        </div>

        {error && <div className="error-banner">{error}</div>}
        {!error && artifacts === null && <p>Loading evidence…</p>}
        {!error && artifacts !== null && artifacts.length === 0 && <p>No evidence recorded for this scenario.</p>}

        {artifacts?.map((artifact) => (
          <div key={artifact.id} className="evidence-item">
            <div className="evidence-item-label">
              {artifact.type} <span className="mono">({artifact.contentType}, {artifact.sizeBytes} bytes)</span>
            </div>

            {artifact.type === "Screenshot" && (
              <img src={evidenceContentUrl(artifact.id)} alt={`${artifact.type} evidence`} className="evidence-screenshot" />
            )}

            {artifact.type === "Log" && (
              <pre className="evidence-text">{logBodies[artifact.id] ?? "Loading…"}</pre>
            )}

            {(artifact.type === "ApiRequest" || artifact.type === "ApiResponse") && (
              <pre className="evidence-text">{jsonBodies[artifact.id] ?? "Loading…"}</pre>
            )}

            {artifact.type === "Trace" && (
              <a href={evidenceContentUrl(artifact.id)} download className="evidence-download">
                Download trace.zip
              </a>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
