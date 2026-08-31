import { apiRequest } from "./client";
import type { CreateRunRequest, RunDetail, RunSummary } from "../types/run";

const BASE_PATH = "/api/runs";

export function getRuns(): Promise<RunSummary[]> {
  return apiRequest<RunSummary[]>(BASE_PATH);
}

export function getRun(id: number): Promise<RunDetail> {
  return apiRequest<RunDetail>(`${BASE_PATH}/${id}`);
}

export function createRun(request: CreateRunRequest): Promise<RunSummary> {
  return apiRequest<RunSummary>(BASE_PATH, {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function stopRun(id: number): Promise<void> {
  return apiRequest<void>(`${BASE_PATH}/${id}/stop`, { method: "POST" });
}
