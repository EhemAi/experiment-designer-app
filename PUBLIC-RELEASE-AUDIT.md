# Experiment Designer v1.1.2 — pre-publication audit

Prepared 2026-10-27 for Emmanuel O. Alagbe. Publication requires the maintainer's
approval. No GitHub push, tag, release, visibility change or Zenodo publication has
been performed. The existing private repository and releases remain private.

The proposed new public repository is
https://github.com/EhemAi/experiment-designer-app. It receives a clean current-source
snapshot with fresh history, not the private repository's commits, tags or assets.

## Findings and disposition

| Finding | Severity / scope | Disposition |
| --- | --- | --- |
| Bundled runtime 10.0.12 behind current Microsoft security releases | Security maintenance; affected components depend on platform/use | Prepared candidate uses .NET 10.0.14 with SDK 10.0.403, verified against Microsoft's SHA-512 metadata; previous working ZIP retained |
| No open-source license or citation policies | Release blocker | Added full MIT license, separate font/runtime notices, citation and Zenodo metadata, security/contribution policies and release checklist |
| SDK/runtime selection and NuGet graph not fully locked | Reproducibility | Exact SDK/runtime pins, five lockfiles, locked restore checks and packaging fallback to an installed SDK |
| Ignored local study/media/dev artifacts could be accidentally included | Privacy | Expanded ignore rules; allowlisted source snapshot omits tools, research files, notes, artifacts, private history and old tags |
| Six Gitleaks matches in third-party local tools | Verified false positives | Two HPKE typed parameter declarations and four example credentials in Gitleaks' own README/ZIP; no actual credentials identified; not part of public source |
| Two Semgrep font-directory traversal warnings | Verified false positives | Only fixed font/license basenames under the application-controlled installation asset directory are read; two narrow inline explanations/suppressions; no runtime behavior changed |
| Dependency update cooldown absent | Low / supply chain hardening | Added seven-day version-update cooldowns; security updates are not delayed by this setting |
| Public/private releases share repository visibility | Privacy / publication blocker | New public repository selected; existing repository and old releases stay private and unchanged |
| Development scanner dependencies contain upstream advisories | Development-only limitation | Semgrep 1.182.1 pins `mcp==1.29.0` (CVE-2026-59951; fixed 1.30.0) and OpenTelemetry 1.37.x (CVE-2026-114603; fixed 1.45.1). Patched versions conflict with upstream exact/range requirements. These are NOT app dependencies, release payload or committed tooling. Semgrep is omitted from CI rather than forcing unsupported overrides. The local scan completed with metrics disabled; MCP server mode was not used. CodeQL and .NET security analyzers provide ongoing C# analysis |
| Audit environment initially had outdated pip | Local development tooling | Updated pip and reran its audit; remaining findings are the two upstream scanner constraints above |
| No DOI yet; reporting channel and GitHub checks not active | Manual publication steps | Valid CFF contains no fabricated DOI; README has a commented badge placeholder. Enable reporting/CI and archive the approved release before adding the real DOI |

## Audit output summaries

- **Gitleaks 8.30.1, full local Git history:** four commits, all branches/tags,
  exit 0, no detected leaks. Read-only remote refs match the audited history.
- **Gitleaks, full local working folder:** about 263 MB scanned including ignored
  artifacts and first-level archive contents; six third-party false positives
  described above. Nested-archive depth warnings are not presented as full recursive
  binary coverage. The separately prepared public snapshot is scanned without a
  baseline or source-secret suppressions before publication approval.
- **NuGet vulnerability audit:** Desktop, Setup and Checks graphs include no
  direct/transitive third-party packages. Official NuGet advisory source was queried.
  Exact framework patch pins and preserved Microsoft notices cover the bundled runtime.
- **pip-audit / npm audit / Bandit:** no Python/JavaScript application projects,
  so these are not application dependency/language checks. A separate pip-audit of
  the ignored development scanner found the upstream tooling issues above; it is
  not described as a clean application audit. No npm project or Python app ships.
- **Semgrep 1.182.1:** default, security-audit and C# packs; 143 rules on 256
  targets; final scan exit 0, zero findings after the two explained false positives.
  Rule packs are fetched from upstream and may evolve. Metrics disabled.
- **Compiler / .NET security analyzers / whitespace formatter:** three production/test
  entry projects checked, zero warnings/errors, formatting verification passed.
- **CFF validation:** cffconvert 2.0.0 accepts CITATION.cff as CFF 1.2.0.
  ORCID and affiliations are those supplied by the author; no identity/contact data
  have been invented. Zenodo's license vocabulary uses `mit`; CFF uses SPDX `MIT`.
- **Regression:** 52,407 core assertions and 686 native desktop assertions passed
  under .NET 10.0.14, including table idle/scroll/page 2, prepared housing, numeric
  Excel IDs, exports, light/dark layouts, replay and failure preservation.

Raw local logs are under ignored `analysis/security/`. They may contain machine
paths and are not publication assets. Physical monitor transitions and physical
printer jobs are not automated; there is no claim that these checks guarantee
absence of every defect or vulnerability.

## Remaining approval and manual actions

Approve the reviewed source snapshot, MIT licensing, version 1.1.2 and candidate
Windows ZIP before any external publishing. The new public repository must be
created and its checks must run on GitHub; local validation is not a CodeQL result.
Follow PUBLIC-RELEASE-CHECKLIST.md to enable secret scanning/push protection,
Dependabot, private vulnerability reporting, main protection and Zenodo integration.
The binaries are unsigned. A direct private contact email can be added if supplied;
the policies currently use the maintainer profile and GitHub private reporting.
Check the release date when approving if publication occurs later.



## Final verification

- Fresh local clone: **52,093 independent assertions passed**. Optional captured
  private Excel fixtures were absent and explicitly skipped. Compiler/security
  analysis and formatting passed; the native app launched and closed cleanly.
- The final snapshot's source and test Git trees equal the clean-tested trees.
- Public working tree and its complete fresh one-commit history: Gitleaks exit 0,
  no detected secrets, no baseline suppressions.
- Exact public-snapshot Semgrep scan: **125 rules, 65 targets, zero findings**.
  It reports six partial-parsing diagnostics (about 99.6% parsed) involving modern
  C# constructs in AllocationReports, AllocationEngine, ProjectSession and the
  uninstaller's raw PowerShell string, plus embedded commands in codeql.yml.
  Ten files are skipped by default ignore patterns, including check-runner tests.
  This is a scanner coverage limitation, not a clean full-parser result. The C#
  compiler, .NET analyzers and runtime tests cover the valid source; CodeQL must
  still run on GitHub. Working application code was not rewritten to appease a parser.
- Citation CI tooling: **17 exact dependency pins**, pip-audit exit 0, no known
  vulnerabilities. This is separate from the two ignored Semgrep-tool advisories.
- YAML and metadata consistency checks passed: three workflows, twelve immutable
  action references, read-only default permissions, consistent version/license/
  repository/ORCID, and valid CFF 1.2.0.
- Windows candidate: portable and installed native checks, 15 Windows integration
  assertions, startup, runtime independence, uninstall and user-file preservation
  passed. All 499 archived payload hashes verified, .NET 10.0.14 and license notices
  present, no research files included. The prior working ZIP is unchanged.
- See [PUBLIC-RELEASE-FILES.md](PUBLIC-RELEASE-FILES.md) for every changed/added file
  and [PUBLIC-RELEASE-CHECKLIST.md](PUBLIC-RELEASE-CHECKLIST.md) for publishing order.

No GitHub or Zenodo publication has occurred. Final release assets identify the
reviewed public source revision; verify the SHA-256 sidecar accompanying the ZIP.
