import { apiRequest } from "./client";
import type { TestDataRecord } from "../types/testData";

export function getTestData(): Promise<TestDataRecord[]> {
  return apiRequest<TestDataRecord[]>("/api/test-data");
}

export function cleanTestDataRecord(id: number): Promise<TestDataRecord> {
  return apiRequest<TestDataRecord>(`/api/test-data/${id}/clean`, { method: "POST" });
}

/** The list must be explicit — an empty list is never sent to mean "clean everything" (TM-06). */
export function cleanTestDataBatch(ids: number[]): Promise<TestDataRecord[]> {
  return apiRequest<TestDataRecord[]>("/api/test-data/clean", {
    method: "POST",
    body: JSON.stringify({ ids }),
  });
}
