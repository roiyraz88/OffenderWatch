export type ReleaseDecision = "Go" | "NoGo" | "Incomplete" | "NoData";

export interface DashboardEnvironmentRun {
  environmentNameSnapshot: string;
  baseUrlSnapshot: string;
  runId: number;
  status: string;
  startedAtUtc: string | null;
  endedAtUtc: string | null;
  durationSeconds: number | null;
  passedCount: number;
  failedCount: number;
  expectedFailedCount: number;
  skippedCount: number;
  totalScenarioCount: number;
  passRate: number | null;
}

export interface DashboardTrendPoint {
  runId: number;
  environmentNameSnapshot: string;
  timestampUtc: string;
  passRate: number | null;
  passedCount: number;
  failedCount: number;
  expectedFailedCount: number;
  totalComparableCount: number;
}

export interface DashboardCurrentlyFailingTest {
  testCaseId: number;
  externalId: string;
  name: string;
  suite: string;
  requirementId: string | null;
  bugId: string | null;
  currentStatus: "Failed" | "ExpectedFail" | string;
  latestRunId: number;
  latestEnvironmentNameSnapshot: string;
  currentFailureSinceUtc: string | null;
  currentFailureSinceRunId: number | null;
  failureDurationSeconds: number | null;
  latestFailureMessage: string | null;
}

export interface Dashboard {
  generatedAtUtc: string;
  overallDecision: ReleaseDecision;
  latestRelevantRunId: number | null;
  latestRunPassRate: number | null;
  latestRunUnexpectedFailedCount: number;
  latestRunExpectedFailedCount: number;
  currentlyFailingTestCount: number;
  latestRunsByEnvironment: DashboardEnvironmentRun[];
  passRateTrend: DashboardTrendPoint[];
  currentlyFailingTests: DashboardCurrentlyFailingTest[];
}
