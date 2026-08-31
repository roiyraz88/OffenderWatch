export interface RunSummary {
  id: number;
  environmentId: number | null;
  environmentNameSnapshot: string;
  baseUrlSnapshot: string;
  status: "Queued" | "Running" | "Completed" | "Stopped" | "Failed";
  trigger: "Manual" | "Api";
  createdAtUtc: string;
  startedAtUtc: string | null;
  endedAtUtc: string | null;
  durationSeconds: number | null;
  passedCount: number;
  failedCount: number;
  expectedFailedCount: number;
  skippedCount: number;
}

export interface ScenarioResult {
  id: number;
  testCaseId: number;
  externalId: string;
  name: string;
  suite: "Ui" | "Api";
  requirementId: string | null;
  bugId: string | null;
  status: "Queued" | "Running" | "Passed" | "Failed" | "ExpectedFail" | "Skipped" | "Cancelled";
  startedAtUtc: string | null;
  endedAtUtc: string | null;
  durationMs: number | null;
  failureMessage: string | null;
  stackTrace: string | null;
}

export interface RunDetail extends RunSummary {
  scenarioResults: ScenarioResult[];
}

export interface CreateRunRequest {
  environmentId: number;
}
