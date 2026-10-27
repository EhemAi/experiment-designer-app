# Experiment Designer v1.1.2

- Numeric treatment IDs now use numeric Excel cells in Movement, Unit BW, Treatment balance, Physical map and all prepared-layout sheets, supporting numeric sorting.
- Leading-zero, alphanumeric and identifiers exceeding Excel's 15-digit precision remain exact text. Saved treatment identities, allocation and prepared housing remain unchanged.

# Experiment Designer v1.1.1

- Corrected the Prepared layout report heading with measured, wrapping layout and a scrollable report body.
- Native print preview starts with one complete fitted page. Printing uses uniform page scaling, avoiding portrait printable-area distortion on landscape pages.
- Simplified Study to experimental design and treatments. Allocation constraints and Notes / metadata controls are removed from view; saved settings and notes are preserved.
- Added a Light mode / Dark mode button with a remembered per-user preference. Switching preserves animal bindings, current filters and pending edits.
- New projects save as `.experimentproj`. Existing `.poultryproj` files open normally and Save prompts for a new filename; their payload, prepared layouts, history and backup validation remain compatible.
- Retains the animal-table tooltip freeze fix, seeded allocation, prepared housing, ANOVA review, exports, installer registration and five-second startup.

# Experiment Designer v1.1.0

Windows x64 release for experimental animal allocation in poultry, swine and other livestock trials.

- Implemented the approved visual redesign across the desktop screens and dialogs, retaining bundled Manrope and Poppins fonts.
- Refined light and dark palettes follow the Windows app-color setting at launch. Restart to use a changed Windows preference.
- Compact cards, navigation, tables, import mappings and export controls retain native keyboard and paging behavior.
- Modernized setup with light/dark styling, visible progress and accessible actions. Installation now creates Start and Windows Installed apps entries, with a durable uninstall option and optional desktop shortcut.
- Five-second branded launch screen with smooth logo animation and a readable Created by Emmanuel Alagbe credit.
- Prepare treatment and pen/cage layouts before animal arrival; print blank allotment sheets and labels.
- Repeating animal allocation retains the prepared treatment, replicate and housing arrangement.
- Automatic heaviest-first animal selection; unused animals are reported as EX.
- Treatment ANOVA on experimental-unit mean body weights appears on screen and beside Unit BW in Excel. A p-value at or below 0.6 recommends repeating with the next seed. Prior results remain available.
- Six Excel result sheets: Summary, Movement, Unit BW, Treatment balance, Physical map and Swap log. Plain numeric IDs, pen/cage numbers and replicates sort as numbers in Excel; identity-sensitive IDs retain exact text.
- Retains the Animals table tooltip fix, with verified idle, mouse-wheel, scrollbar, keyboard and page navigation.
- Portable app, per-user installer, bundled .NET runtime and offline user guide. Distribution filenames use Experiment Designer.

Extract the complete ZIP. Run `App/ExperimentDesigner.exe` for portable use, or `Setup/ExperimentDesigner.Setup.exe` to install/update for your Windows account. No separate runtime or SDK is required.

Existing `.poultryproj` files remain supported. New automatic-selection and prepared-layout metadata require this release or a compatible later version. Allocation generator identities remain stable for replay. Save projects explicitly; there is no autosave. The app is unsigned and managed through the included Setup app.

The p-value is a baseline balance diagnostic; a high p-value does not prove identical treatment means or scientific success. Repeating is an explicit researcher action, not an automatic search for a desired p-value.

Private source workbooks, projects, test captures and local development tools are excluded from the source repository. The release archive contains the application and runtime, not private study data.
