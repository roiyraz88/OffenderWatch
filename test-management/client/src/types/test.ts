export interface TestCaseSummary {
  id: number;
  externalId: string;
  name: string;
  suite: "Ui" | "Api";
  requirementId: string | null;
  bugId: string | null;
  lastStatus: string | null;
  lastRunId: number | null;
  lastExecutedAtUtc: string | null;
  isFlaky: boolean;
  currentFailureSinceRunId: number | null;
  currentFailureSinceUtc: string | null;
  lastPassRunId: number | null;
  lastPassAtUtc: string | null;
}

export type HistoryTransition = "FirstResult" | "Regression" | "Recovery" | "StillFailing" | "StillPassing" | "Neutral";

export interface TestHistoryEntry {
  runId: number;
  environmentNameSnapshot: string;
  runStartedAtUtc: string | null;
  scenarioResultId: number;
  status: string;
  startedAtUtc: string | null;
  endedAtUtc: string | null;
  durationMs: number | null;
  failureMessage: string | null;
  transition: HistoryTransition;
}

export interface TestCaseDetail extends TestCaseSummary {
  history: TestHistoryEntry[];
}
