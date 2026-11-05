# Maintainer release checklist

Publication requires the maintainer's authorization. The v1.1.5 publication was
explicitly requested; no additional approval is required for that release.

1. Preserve the MIT license, author-supplied ORCID/affiliations and separate
   font/runtime notices. Update product version, citation date, release metadata,
   changelog and download links consistently.
2. Scan the working folder and private/public Git histories. Investigate every
   match. Never publish credentials, research inputs, local projects, tools,
   generated outputs, private history or private tags.
3. Prepare updates in the existing public `EhemAi/experiment-designer-app`
   repository. Keep `EhemAi/experiment-designer` and its old releases private.
4. Run independent tests, optional private fixture checks separately, locked
   restores, formatter/compiler/security diagnostics, dependency audits, Semgrep,
   metadata validation, native desktop and installation/update/uninstall checks.
   Test a fresh checkout using the README instructions.
5. Verify the complete Windows archive, all payload hashes, runtime provenance,
   source revision, license notices, startup and user-file preservation. Retain
   previous releases for rollback. Binaries remain unsigned.
6. Push a release branch and use a protected PR. Require green `windows`,
   `citation`, `csharp` (CodeQL), `secrets` and `dependency-review` checks. Do not
   bypass branch protection or force-push main.
7. Confirm secret scanning/push protection, Dependabot, private vulnerability
   reporting and the existing Zenodo GitHub integration remain enabled.
8. Release v1.1.5 from the verified public main commit. Upload the ready-to-run
   Windows ZIP/checksum and source ZIP/checksum. Verify anonymous downloads and
   their hashes. Keep the README download prominent.
9. Verify Zenodo archives the new source release in the existing concept DOI
   series (10.5281/zenodo.23698895). Confirm version, title, author, ORCID,
   affiliations, license, citation request and exact source-tree contents.
   Do not create a duplicate concept record. Windows binaries are distributed
   on GitHub; verify rather than assuming they were archived on Zenodo.
10. Add the actual version DOI to README/CITATION in a protected follow-up PR.
    The archived source is immutable; DOI-only follow-ups do not rewrite it.

Document tool advisories, parser coverage limits, unsigned binaries and tests
that require a second physical computer, monitor transitions or a real printer.
Do not describe those limitations as checks that passed.
