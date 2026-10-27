# v1.1.0 Windows installer correction

Verified on Windows x64, 5 October 2026.

- 15 Windows integration checks passed: current-user Installed apps metadata, Start menu shortcut, optional desktop shortcut, update targets, foreign shortcut preservation and removal.
- The exact registered uninstall command ran successfully with spaces, Unicode and long installation paths, without the original source ZIP or a global runtime. It preserved study files and unknown maintenance files, and removed owned app entries and shortcuts.
- Modern setup was rendered in light and dark at 100%, 125%, 150% and 200% programmatic scale. Inputs and buttons were checked against their containers, and every visible action against the actual window viewport. Actions remain pinned while content can scroll. Physical monitor changes were not automated.
- Published and installed desktop builds passed 669 smoke assertions, including the supplied workbook idle/scroll regression, page 2, all existing stages/tabs/dialogs, reports and five-second startup.
- Application, allocation engine and application-core C# source files were not changed. Four application binaries carry the newer Git revision in build metadata; the scientific logic, data bindings and workflows remain the approved v1.1.0 implementation.
- All 497 app/runtime payload hashes match the final manifest. No private workbooks or projects are included.

Windows app-entry field reference: https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key

Archive: Experiment-Designer-installer-fixed-1.1.0-win-x64-20261005-130710.zip
SHA-256: 1452A6AF447A136DB6E48BB657DD8CBFCA6CFECED2FC1EE12991F999B8DBE5FC

Update an existing copy by running the new Setup and choosing the same installation folder. Close the app first.
