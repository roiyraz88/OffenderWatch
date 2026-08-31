import { API_BASE_URL, apiRequest } from "./client";
import type { EvidenceArtifact } from "../types/evidence";

export function getScenarioEvidence(runId: number, scenarioResultId: number): Promise<EvidenceArtifact[]> {
  return apiRequest<EvidenceArtifact[]>(`/api/runs/${runId}/scenarios/${scenarioResultId}/evidence`);
}

/** TM-08 (6.18) — the browser fetches actual bytes directly through this URL; the API never returns a filesystem path. */
export function evidenceContentUrl(evidenceArtifactId: number): string {
  return `${API_BASE_URL}/api/evidence/${evidenceArtifactId}/content`;
}
