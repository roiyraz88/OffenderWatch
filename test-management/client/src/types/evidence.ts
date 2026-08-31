export type EvidenceType = "Log" | "Screenshot" | "ApiRequest" | "ApiResponse" | "Trace";

export interface EvidenceArtifact {
  id: number;
  scenarioResultId: number;
  type: EvidenceType;
  contentType: string;
  sizeBytes: number;
  createdAtUtc: string;
}
