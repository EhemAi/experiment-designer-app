# Public release checklist — approval required

Nothing in this checklist automatically pushes, tags, changes repository visibility
or publishes. Review the final security report and approve the exact candidate.

## Before publishing

1. Confirm MIT licensing and that the original app and branding can be released.
   Preserve the separately licensed font and Microsoft runtime notices.
2. Confirm author Emmanuel O. Alagbe, ORCID 0000-0002-0747-2866,
   Purdue University and The Ohio State University. A direct private contact email
   is optional and has not been invented. Check CITATION.cff's date if publication
   is later than the prepared date; keep all metadata consistent.
3. Review the source candidate and clean-clone checks. Exclude all research inputs,
   local notes, videos, tool environments, generated outputs and private projects.
4. Keep the existing EhemAi/experiment-designer repository and all old releases private.
   Publish only the reviewed current snapshot in a NEW EhemAi/experiment-designer-app
   repository with a fresh initial commit. Do not push private history or old tags.
   The private history audit found no detected credentials; it stays private.
5. Retain the previous working ZIP privately. Use only the security-prepared ZIP
   and its checksum for the public v1.1.2 release. Code signing is not yet configured.

## GitHub settings (maintainer)

- After approval, create EhemAi/experiment-designer-app and push the clean initial
  source snapshot to main. Keep EhemAi/experiment-designer and its old releases
  private. Review later changes through pull requests.
- Enable dependency graph, Dependabot alerts and security updates.
- Enable secret scanning and push protection.
- Enable private vulnerability reporting and test the SECURITY.md report link.
- Enable CodeQL code scanning and Actions. The committed workflows have not run on
  GitHub until pushed; require their successful runs before the release.
- Protect main with required pull requests, resolved conversations and the test,
  source-quality, CodeQL, secret-scan and applicable dependency-review checks.
  Disable force pushes/deletions; select status contexts after their first run.

## Zenodo and release order

1. Sign in to Zenodo with GitHub and authorize the account/repository connection.
   In Zenodo's GitHub settings, enable **EhemAi/experiment-designer-app** before creating
   the new GitHub release. Ensure the repository is public and metadata is present.
2. After approval and green CI, create the **new v1.1.2 tag and GitHub release** on
   the approved main commit. Use `releases/v1.1.2.md` and attach the reviewed Windows
   ZIP and SHA-256 sidecar. Do not copy the old private tags or releases to the new repository.
3. Wait for Zenodo to archive that GitHub release. Verify title, version, license,
   author, affiliations and ORCID. Confirm the archived source and separately
   distributed Windows asset; do not assume GitHub binary assets were archived.
4. Record both the version DOI and concept DOI. Add the real version DOI to
   CITATION.cff and replace README's commented badge placeholder with Zenodo's
   actual DOI badge/link. Use the version DOI when citing this exact release.
5. Commit the DOI-only follow-up to GitHub. GitHub source snapshots already archived
   on Zenodo do not change retroactively; edit the Zenodo record's descriptive
   metadata if needed, and include completed citation metadata in later versions.
6. If a public source archive must already contain the DOI, reserve a DOI in a
   manual Zenodo draft and choose that explicit deposition path instead of assuming
   the automatic GitHub snapshot will be rewritten. Do not create duplicate records.

References: [Zenodo GitHub software archival](https://help.zenodo.org/docs/github/archive-software/github-upload/),
[GitHub citation files](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-citation-files).
