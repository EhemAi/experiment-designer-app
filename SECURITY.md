# Security policy

## Supported versions

| Version | Security fixes |
| --- | --- |
| 1.1.2 | Current supported line |
| Earlier releases | Upgrade to the current supported release |

Use the security-prepared public release and its checksum when available. The
older development ZIP with the same app version contains an older bundled
runtime. Runtime updates require a new reviewed distribution.

## Private reports

Use [GitHub private vulnerability reporting](https://github.com/EhemAi/experiment-designer-app/security/advisories/new).
This channel must be enabled by the maintainer before public launch. If it is
unavailable, contact the maintainer through the contact details on
[EhemAi's GitHub profile](https://github.com/EhemAi) to arrange a private channel.
Do not post a vulnerability, credentials or study data in a public issue.

Include the version, affected workflow, a minimal synthetic reproduction and
potential impact. Avoid attaching animal records or researcher data. Reports
will be investigated and coordinated privately; there is no guaranteed response
time or bounty program.

## Trust boundaries

The app works locally and does not call external APIs, LLMs, analytics or update
services. It has no API keys or server configuration. Workbook macros and
formulas are never executed. Imports and project files are bounded and validated;
project replay checks consistency. Checksums detect corruption, not authenticity.
Download software only from trusted releases and do not run a modified installer
merely because its manifest verifies. The Windows binaries are currently unsigned.

Project files retain source records and history and can contain personal or
research data. Keep these files private. No project file encryption is provided.
