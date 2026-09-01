import { apiRequest } from "./client";
import type { RunComparison } from "../types/runComparison";

/** Bonus B-02 — GET /api/runs/compare?baseRunId=&compareRunId=. Read-only; never mutates either run. */
export function getRunComparison(baseRunId: number, compareRunId: number): Promise<RunComparison> {
  const params = new URLSearchParams({ baseRunId: String(baseRunId), compareRunId: String(compareRunId) });
  return apiRequest<RunComparison>(`/api/runs/compare?${params.toString()}`);
}
