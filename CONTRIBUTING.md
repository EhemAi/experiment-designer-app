# Contributing

Read the README, SECURITY.md and CODE_OF_CONDUCT.md before contributing.
Use public issues for reproducible bugs and feature requests with synthetic data;
use the private security channel for vulnerabilities.

## Development

On Windows x64, install the SDK pinned in `global.json` and run:

```powershell
./build-and-check.ps1
./check-source.ps1
```

`-SkipDesktop` runs the platform-independent engine/controller checks. Native
desktop checks require a Windows desktop session and create their own synthetic
fixtures and renders under ignored `analysis/`. Optional private-workbook checks
may be skipped; never commit those workbooks, outputs or local paths.

Run `./format-source.ps1` for whitespace formatting, then repeat the checks.
Keep semantic changes focused, document behavior changes in CHANGELOG.md, and
include tests for allocation invariants, import validation or export identities
when those behaviors change. Do not rewrite saved schemas, seeded randomness,
prepared housing mappings or installer ownership conventions as cosmetic cleanup.

Open a pull request describing the problem, resulting behavior and validation.
Contributions are provided under the repository's MIT license; third-party assets
must include compatible licenses and attribution. Do not submit confidential
research, employer-owned code without permission, or credentials.
