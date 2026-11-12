# Experiment Designer v1.1.6 publication

- [GitHub release](https://github.com/EhemAi/experiment-designer-app/releases/tag/v1.1.6)
- [Windows x64 ZIP](https://github.com/EhemAi/experiment-designer-app/releases/download/v1.1.6/Experiment-Designer-1.1.6-win-x64.zip)
- [Version DOI](https://doi.org/10.5281/zenodo.24175085) and [all-versions DOI](https://doi.org/10.5281/zenodo.23698895)
- Released source commit `48102bf027c1976ba65b84772f6715e2c0f67b11`.
- Windows ZIP SHA-256 `ae55c912803e9f36aa81805e8a5ba708272501ed09c33eeafbfbe9c1448b1b94`.
- Source ZIP SHA-256 `df18fad5fa9d791e2c063df9595f4f2b1abbd54c92d83968c743035b3f7bea4a`.
- All four GitHub assets downloaded anonymously and matched their expected hashes. The Zenodo archive contains 134 files that match the released Git blobs byte for byte.
- Clean checkout and release source passed 61,139 engine assertions. The native Windows checks passed 742 assertions. Packaged startup, portable launch, installation, update and uninstall passed with the bundled .NET 10.0.15 runtime.
- Verified 520 Windows archive files, 499 manifest payloads and 480 runtime files against the official SDK archive. The app and installer embed the released Git SHA.
- Protected PR and main checks passed. Semgrep ran 143 rules over 112 targets with zero findings and approximately 99.8% parser coverage. Gitleaks reported zero findings in the public checkout and history. The original repository history also had zero findings. Six matches in ignored local tools were examples or type annotations. The local archive scan had a one-level depth limit and format warnings in SDK assets. No known vulnerabilities were reported for shipped app projects or the pinned citation dependencies.
- Isolated Semgrep tooling reported CVE-2026-114603, CVE-2026-116684, CVE-2026-116685, CVE-2026-116686, CVE-2026-59951. Those Python tooling dependencies are outside the release package. The Windows binaries are unsigned. Native testing used one Windows computer and programmatic DPI checks. No physical monitor transitions or printer jobs were tested.
- [v1.1.5 publication record](https://github.com/EhemAi/experiment-designer-app/blob/afd96cf/PUBLICATION-REPORT.md)
