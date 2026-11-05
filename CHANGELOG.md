# Changelog

## [1.1.5] - 2026-11-04

### Added
- Optional sex import/entry, normalized Male/Female/Unknown values, filtering and reports.
- Ignore, Balance and Separate sex allocation, explicit pen composition and two replicate groups.
- Ranked BW subgroup RCBD search: comparable pen distributions, matched means, range comparison and an automatic 0.5% block CV target with visible unattainable-target feedback.
- Replicate BW export and pen minimum/maximum BW alongside means and SD.

### Fixed
- Reject overlong Windows shortcut/font paths before installation, avoiding silent shortcut truncation and native font-loading failures.

### Changed
- Heaviest-first selection respects specified sex counts without adding light EX animals for balance.
- New RCBD runs record their grouping rule while historical algorithms/results replay unchanged.
- New BW imports retain entered numbers; UI and report labels impose no measurement unit.
- Canonical numeric old-pen values export numerically; leading-zero and precision-sensitive codes retain identity.
- Prepared Excel contains only Layout; result Excel omits Physical map and unused Swap log.
- Preserve fixed prepared treatment locations across reruns and existing male/female block order.

## 1.1.2 — public release candidate

- Numeric treatment IDs use numeric Excel cells across allocation and prepared-layout sheets; identity-sensitive IDs retain exact text.
- Prepare MIT licensing, citation/Zenodo metadata, security and contribution policies, and automated checks for public release.
- Update the bundled .NET security patch and verify the existing native app behavior.

## 1.1.1 — 2026-10-27

- Corrected prepared-layout headings and complete-page print preview.
- Added a remembered light/dark theme toggle and simplified Study controls.
- New `.experimentproj` projects retain compatibility with legacy `.poultryproj` files.

## 1.1.0 — 2026-10-05

- Implemented the native desktop redesign and modernized per-user setup.
- Added Windows Installed apps, Start-menu and durable uninstall integration.
- Retained prepared layouts, animal-table responsiveness and five-second startup.

## 1.0.5 — 2026-10-04

- Refined launch branding, result balance feedback and release naming.
- Preserved prepared pen/cage locations across allocation reruns.

## 1.0.0 — 2026-10-03

- Initial private release: seeded animal allocation, source imports, project history, physical-unit mapping and exports.

The earlier tags were private development releases. See RELEASE-NOTES.md for
details; a DOI is added only after the approved public release is archived.
