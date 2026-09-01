import type { RunSummary } from "./run";

export interface MetricDelta {
  base: number;
  compare: number;
  delta: number;
}

export interface TotalsDelta {
  passed: MetricDelta;
  failed: MetricDelta;
  expectedFail: MetricDelta;
  skipped: MetricDelta;
  total: MetricDelta;
}

export interface ComparisonSummary {
  regressions: number;
  recoveries: number;
  new: number;
  missing: number;
  stillPassing: number;
  stillFailing: number;
  expectedFailures: number;
  otherChanges: number;
  unchanged: number;
}

export type ComparisonChangeType =
  | "New"
  | "Missing"
  | "Regression"
  | "Recovery"
  | "StillPassing"
  | "StillFailing"
  | "ExpectedFailure"
  | "Unchanged"
  | "OtherChange";

export interface TestComparisonEntry {
  testCaseId: number;
  externalId: string;
  name: string;
  suite: "Ui" | "Api";
  requirementId: string | null;
  bugId: string | null;
  baseStatus: string | null;
  compareStatus: string | null;
  change: ComparisonChangeType;
}

export interface RunComparison {
  baseRun: RunSummary;
  compareRun: RunSummary;
  environmentsDiffer: boolean;
  baseRunIncomplete: boolean;
  compareRunIncomplete: boolean;
  totalsDelta: TotalsDelta;
  summary: ComparisonSummary;
  tests: TestComparisonEntry[];
}
