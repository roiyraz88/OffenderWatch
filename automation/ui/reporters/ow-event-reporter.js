// Part 5 (Step 4 / TM-02) structured runner-event reporter.
//
// Emits one JSON object per line, prefixed with "OW_EVENT|", to stdout for
// every scenario_discovered / scenario_started / scenario_finished /
// suite_finished event — the machine-readable protocol the ASP.NET Core
// orchestrator parses (never human-readable Playwright console output).
// Added alongside the existing list/html/json reporters in
// playwright.config.js; it doesn't replace or change them.
const fs = require('fs');
const path = require('path');
const { ATTACHMENT_NAME: TEST_DATA_ATTACHMENT_NAME } = require('./test-data-capture');

// Part 5 (Step 6 / TM-08) — where the orchestrator told us to write this
// run's evidence. Optional: unset when this suite is run by hand outside
// the platform, in which case evidence capture is simply skipped (6.14).
const ARTIFACT_DIR = process.env.OFFENDERWATCH_ARTIFACT_DIR;

function sanitize(externalId) {
  // A safe, deterministic folder name for one scenario's evidence — never
  // the raw spec path/title verbatim into a filesystem path (6.14).
  return externalId.replace(/[^A-Za-z0-9_.-]+/g, '_').replace(/^_+|_+$/g, '');
}

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

    this.writeEvidence(meta.externalId, test, result, status, failureMessage, stackTrace);
    this.emitTestDataCreated(meta.externalId, result);
  }

  emitTestDataCreated(scenarioExternalId, result) {
    // Part 5 (Step 7 / TM-06) — explicit ownership only: reads back exactly
    // what the spec itself registered via testInfo.attach() at its actual
    // successful-creation point (test-data-capture.js). Emitted regardless
    // of pass/fail status, same as the API side.
    for (const attachment of result.attachments || []) {
      if (attachment.name !== TEST_DATA_ATTACHMENT_NAME || !attachment.body) continue;
      let payload;
      try {
        payload = JSON.parse(attachment.body.toString('utf-8'));
      } catch {
        continue;
      }
      this.emit({
        eventType: 'test_data_created',
        externalId: scenarioExternalId,
        entityType: payload.entityType,
        entityExternalId: payload.entityExternalId,
        entityIdentifier: payload.entityIdentifier,
      });
    }
  }

  emitArtifact(externalId, artifactType, absolutePath, contentType) {
    // Path is reported relative to OFFENDERWATCH_ARTIFACT_DIR — the
    // orchestrator resolves and validates it against that same
    // run-specific directory before trusting it (6.13).
    const relativePath = path.relative(ARTIFACT_DIR, absolutePath).replace(/\\/g, '/');
    this.emit({
      eventType: 'artifact_created',
      externalId,
      artifactType,
      path: relativePath,
      contentType,
    });
  }

  writeEvidence(externalId, test, result, status, failureMessage, stackTrace) {
    if (!ARTIFACT_DIR) return; // standalone run outside the platform (6.14) — nothing to write

    const scenarioDir = path.join(ARTIFACT_DIR, sanitize(externalId));
    fs.mkdirSync(scenarioDir, { recursive: true });

    // 6.17 — one execution log per scenario, never a shared/mutable file.
    const stdout = (result.stdout || []).map((c) => c.toString()).join('');
    const stderr = (result.stderr || []).map((c) => c.toString()).join('');
    const steps = (result.steps || [])
      .map((s) => `  - ${s.title} (${s.duration}ms)${s.error ? ' [FAILED]' : ''}`)
      .join('\n');

    const logLines = [
      `test: ${test.title}`,
      `location: ${test.location.file}:${test.location.line}`,
      `status: ${status}`,
      `durationMs: ${result.duration}`,
    ];
    if (steps) logLines.push('', '--- steps ---', steps);
    if (stdout) logLines.push('', '--- stdout ---', stdout);
    if (stderr) logLines.push('', '--- stderr ---', stderr);
    if (failureMessage) logLines.push('', `failureMessage: ${failureMessage}`);
    if (stackTrace) logLines.push('', '--- stack trace ---', stackTrace);

    const logPath = path.join(scenarioDir, 'execution.log');
    fs.writeFileSync(logPath, logLines.join('\n'), 'utf-8');
    this.emitArtifact(externalId, 'Log', logPath, 'text/plain');

    // 6.11/6.15 — a final screenshot for every scenario (screenshot:'on' in
    // playwright.config.js), plus a trace for failures where one was
    // produced (trace:'retain-on-failure'). Both come from Playwright's own
    // test-result attachments — no test file needed to change.
    for (const attachment of result.attachments || []) {
      if (!attachment.path || !fs.existsSync(attachment.path)) continue;

      if (attachment.name === 'screenshot') {
        const dest = path.join(scenarioDir, 'final.png');
        fs.copyFileSync(attachment.path, dest);
        this.emitArtifact(externalId, 'Screenshot', dest, attachment.contentType || 'image/png');
      } else if (attachment.name === 'trace') {
        const dest = path.join(scenarioDir, 'trace.zip');
        fs.copyFileSync(attachment.path, dest);
        this.emitArtifact(externalId, 'Trace', dest, 'application/zip');
      }
    }
  }

  onEnd(_result) {
    this.emit({ eventType: 'suite_finished' });
  }
}

module.exports = OwEventReporter;
