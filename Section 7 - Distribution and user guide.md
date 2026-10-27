# Section 7 — Windows distribution and user guide

The Windows x64 distribution includes the desktop app, a per-user setup program, the .NET 10.0.14 Windows runtime, professional fonts and their licenses, and an offline user guide accessible from the application's header. The package runs without an SDK or separate runtime installation.

## Use

Extract the whole ZIP. Open `App/ExperimentDesigner.exe` for portable use, or `Setup/ExperimentDesigner.Setup.exe` to install for your account. The default destination is `%LOCALAPPDATA%/ExperimentDesigner`. Installation requires no administrator access and can create a desktop shortcut. Keep App and Runtime together.

The installer uses separate release folders and publishes its receipt only after all payload files pass SHA-256 verification. Updates retain previous releases. Copy failures or cancellation preserve the active release. The installer refuses traversal, linked paths and invalid receipts. Checksums detect corruption; they are not authenticity signatures.

To remove the program, close it and rerun Setup with the same destination. Removal deletes only unchanged files named by validated manifests. User projects, unknown files and modified files remain. Busy files leave the receipt and manifests available for retry. Installed copies are registered in Start and Windows Installed apps and can also be managed through Setup. The application is unsigned.

## Build and verification

Run `./build-package.ps1` from the workspace. It runs engine/controller/native desktop checks, publishes app-relative runtime hosts, bundles the local runtime without its SDK, verifies every payload hash, and runs the published portable and installed applications with global runtime discovery disabled. It also renders the setup interface. The archive and SHA-256 sidecar are placed in `dist`; `dist/latest-package.txt` identifies the latest archive. `-SkipChecks` reuses the already-built check runner and skips the full check suite, but still validates the published package and installed application.

The complete suite passed **52,225 assertions**, including 21 installation assertions, plus **39 native desktop smoke assertions**. Installer tests cover corruption, unsafe paths/receipts, successful installation/update, interrupted copy rollback, busy-file retry, and preservation of project/unknown/modified files. Published portable and installed apphosts pass the same 39 native desktop assertions using the adjacent bundled runtime. Package verification records are under `analysis/package-validation`; installer fixtures are under `analysis/installation-validation`.

The native renders cover all four main stages, minimum desktop width, reviewed imports, edits, swaps, mapping, report controls and print pagination. The new guide button fits at the minimum supported width. Setup uses the same charcoal palette, Manrope/Poppins fonts and compact interactive targets. All builds completed without warnings or errors. No actual printer jobs, real-study allocations, desktop shortcuts or installation outside the workspace were created during verification.

The original macro workbook remains byte-identical, with SHA-256 `3A8B964C0877643BB1B94EA952B1AA055A87F2E0DADB650D74337460A57B1686`.

## Remaining operational decisions

Technical implementation and synthetic workflow verification are complete. Final scientific acceptance requires the researcher's real-study settings, explicit approved animal subset and housing declarations. The guide explains these steps, project saving/recovery, exports, printing and troubleshooting. Unsaved work is not autosaved; save projects regularly in a separate study folder. Code signing and enterprise deployment are outside this distribution.
