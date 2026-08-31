// Part 5 (Step 4 / TM-02) structured runner-event reporter.
//
// Emits one JSON object per line, prefixed with "OW_EVENT|", to stdout for
// every scenario_discovered / scenario_started / scenario_finished /
// suite_finished event — the machine-readable protocol the ASP.NET Core
// orchestrator parses (never human-readable Playwright console output).
// Added alongside the existing list/html/json reporters in
// playwright.config.js; it doesn't replace or change them.
const path = require('path');

function extractMeta(test) {
  const specPath = path
    .relative(process.cwd(), test.location.file)
    .replace(/\\/g, '/');
  // ui::<spec file>::<test title> — stable across runs, no run-specific
  // data (matches the api::<pytest nodeid> convention on the API side).
  const externalId = `ui::${specPath}::${test.title}`;

  const requirementMatch = test.title.match(/^((?:FR|API)-\d+)/);
  // This repo's convention is a trailing "[BUG-xxx]" (sometimes
  // "[BUG-xxx / BUG-yyy]") at the end of the test title.
  const bugMatch = test.title.match(/\[([^\]]+)\]\s*$/);

  return {
    externalId,
    name: test.title,
    requirementId: requirementMatch ? requirementMatch[1] : null,
    bugId: bugMatch ? bugMatch[1] : null,
  };
}

class OwEventReporter {
  emit(event) {
    const payload = {
      version: 1,
      runner: 'playwright',
      timestampUtc: new Date().toISOString(),
      ...event,
    };
    process.stdout.write('\nOW_EVENT|' + JSON.stringify(payload) + '\n');
  }

  onBegin(_config, suite) {
    for (const test of suite.allTests()) {
      const meta = extractMeta(test);
      this.emit({
        eventType: 'scenario_discovered',
        externalId: meta.externalId,
        name: meta.name,
        suite: 'UI',
        requirementId: meta.requirementId,
        bugId: meta.bugId,
      });
    }
  }

  onTestBegin(test) {
    const meta = extractMeta(test);
    this.emit({ eventType: 'scenario_started', externalId: meta.externalId });
  }

  onTestEnd(test, result) {
    const meta = extractMeta(test);

    let status;
    if (result.status === 'passed') status = 'passed';
    else if (result.status === 'skipped') status = 'skipped';
    else status = 'failed'; // 'failed' or 'timedOut' — both are real failures

    const firstError = result.errors && result.errors[0];
    const failureMessage = firstError ? String(firstError.message || firstError).split('\n')[0] : null;
    const stackTrace =
      result.errors && result.errors.length
        ? result.errors.map((e) => e.stack || e.message || String(e)).join('\n---\n')
        : null;

    this.emit({
      eventType: 'scenario_finished',
      externalId: meta.externalId,
      status,
      durationMs: result.duration,
      failureMessage,
      stackTrace,
    });
  }

  onEnd(_result) {
    this.emit({ eventType: 'suite_finished' });
  }
}

module.exports = OwEventReporter;
