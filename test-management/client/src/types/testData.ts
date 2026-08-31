export type TestDataEntityType = "Offender" | "LocationPoint";
export type TestDataCleanupStatus = "Active" | "Cleaned" | "CleanupFailed";

export interface TestDataRecord {
  id: number;
  testRunId: number;
  scenarioResultId: number | null;
  environmentNameSnapshot: string;
  entityType: TestDataEntityType;
  externalId: string | null;
  identifier: string | null;
  createdAtUtc: string;
  cleanedAtUtc: string | null;
  cleanupStatus: TestDataCleanupStatus;
}
