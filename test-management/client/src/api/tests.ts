import { apiRequest } from "./client";
import type { TestCaseDetail, TestCaseSummary } from "../types/test";

export function getTests(): Promise<TestCaseSummary[]> {
  return apiRequest<TestCaseSummary[]>("/api/tests");
}

export function getTestHistory(id: number): Promise<TestCaseDetail> {
  return apiRequest<TestCaseDetail>(`/api/tests/${id}/history`);
}
