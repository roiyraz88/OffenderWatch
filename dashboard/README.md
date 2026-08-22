# QA Dashboard

`dashboard.html` is a self-contained, static snapshot of the testing-phase
dashboard — open it directly in any browser, no server or build step needed.

It is populated from the real results in:
- `../OffenderWatch_Tests.xlsx` (sheets: Test Cases Final, Execution Results
  Final, Bug Reports, Final Summary)
- `../automation/ui` and `../automation/api` — latest automation run
  (11 UI + 22 API scenarios)

Live version (private link): https://claude.ai/code/artifact/c7af2132-a830-4823-a7cf-92db5b7f6817

To refresh the numbers after a new test cycle: re-run both automation
suites, re-tally pass/fail/defect counts from the workbook, and edit the
`rows` array and stat tiles in `dashboard.html` accordingly — it has no
build step, everything is inline HTML/CSS/JS.
