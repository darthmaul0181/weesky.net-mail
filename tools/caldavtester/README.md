# CardDAV conformance harness

This is the 4d CardDAV conformance harness: it runs Apple's `ccs-caldavtester`
against a real dev deployment's CardDAV endpoint. See
`docs/superpowers/specs/2026-08-31-webmail-contacts-4d-conformance-design.md`
for why this exists and how results are read into a report.

## Prerequisites

- PowerShell 7.
- Python 2.7.18 — the tester is Python 2 and will not run on anything else.
  Install from `https://www.python.org/downloads/release/python-2718/`
  (Windows x86-64 MSI installer). Check with `py -2.7 -V`.
- git.

## The dedicated dev account

Create a user on dev, enable the Sync tab, and copy the three values it shows
(account GUID, email, DAV secret) into `serverinfo.local.json`, which you
create by copying `serverinfo.local.example.json`.

**Never use a personal account: every run empties its address book.**
Regenerate the secret once the test campaign ends.

## Run

```powershell
pwsh -File tools/caldavtester/run.ps1                          # all suites
pwsh -File tools/caldavtester/run.ps1 -Suites CardDAV/propfind.xml   # one suite
pwsh -File tools/caldavtester/run.ps1 -SetupOnly                # clone + config only
pwsh -File tools/caldavtester/run.ps1 -PrintResponses            # verbose replay of a failure
```

## Output

Each run writes `results/<timestamp>.txt`. `Authorization` header lines are
scrubbed before the file touches disk. Whatever goes into the conformance
report is copied from this file only — never from the live console.

## Reading results

Each test prints `[FAILED]` or `[OK]`. A test file whose `<start>` step fails
is skipped entirely (nothing under it ran). `mkcol.xml`, `copymove.xml` and
`aclreports.xml` are EXPECTED to fail — see the named divergences in the spec.
