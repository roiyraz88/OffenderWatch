// A small, safe ANSI escape/control-sequence stripper — DISPLAY ONLY. Never
// used anywhere evidence is fetched/stored/persisted; call sites apply it
// only at the point of rendering text in the UI, so the original evidence
// (on disk and in SQLite) is never touched.
//
// Playwright/Jest terminal output (failure messages, stack traces, and
// execution logs captured verbatim from real process stdout) commonly
// contains ANSI CSI sequences for color/style, e.g. "\x1b[2m", "\x1b[31m",
// "\x1b[39m" — without this, those render as visible garbage in the
// browser instead of being interpreted as (invisible) terminal formatting.
//
// This is the same well-established CSI/OSC pattern the widely-used
// `ansi-regex` npm package uses, reproduced locally so no dependency is
// added just for this. It only matches real escape/control bytes
// (/-led sequences) — normal Unicode text, punctuation, and
// whitespace are never touched.
const ANSI_PATTERN = new RegExp(
  "[\\u001B\\u009B][[\\]()#;?]*(?:(?:(?:[a-zA-Z0-9]*(?:;[a-zA-Z0-9]*)*)?\\u0007)" +
    "|(?:(?:[0-9]{1,4}(?:;[0-9]{0,4})*)?[0-9A-PR-TZcf-ntqry=><~]))",
  "g",
);

export function stripAnsi(text: string): string {
  return text.replace(ANSI_PATTERN, "");
}
