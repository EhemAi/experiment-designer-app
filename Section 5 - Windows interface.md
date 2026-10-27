# Section 5 — Windows interface

The Windows application implements the study-to-allocation workflow on the Section 3 engine and Section 4 project format. Launch it with `./run-designer.ps1` from this directory in PowerShell. The script builds with the local .NET SDK and opens the application. The release ZIP also includes a portable app, a per-user installer and a bundled runtime.

## Visual system

Version 1.1.1 implements the approved light and dark redesign. The header Light mode / Dark mode button switches appearance without reloading records or discarding drafts. The choice is remembered per user; Windows supplies the initial default. All four stages and dialogs share layered surfaces, compact rows, subtle borders and teal primary and navigation accents. Manrope supplies body and table text; Poppins supplies headings. The application bundles the fonts and OFL licenses and loads fonts privately within its own process. Windows title bars, scroll bars, file pickers and system confirmation messages retain native styling.

Desktop spacing uses tight headers and explanatory text, compact file actions, 34-pixel minimum input/button targets, 38-pixel table rows, 40-pixel table headers, and cell padding of 9 pixels horizontally and 5 vertically. Table views have an 8-pixel top inset. Interactive targets are reapplied after initial Windows DPI/font scaling. Sidebar spacing and action groupings remain compact. The report dialog also uses tight vertical spacing.

## Workflow

1. **Study:** choose RCBD, completely randomized or conventional allocation; define pen/cage units, units per treatment, animals per unit and ordered treatment IDs/labels. Internal allocation settings remain preserved in existing projects; optimization and notes controls are omitted from the Study screen. Apply settings. Incomplete projects can be saved as drafts.
2. **Animals:** choose an import source, sheet, exact rows, columns, weight units and numeric-ID policy. Review validation before applying the complete import. Imports automatically prioritize the heaviest required pool, retaining unused animals as EX. Manual selection remains available for existing manual-selection studies; draft decisions survive paging and filtering.
3. **Allocation:** prepare and print the treatment and housing layout before animal arrival, leaving animal IDs and weights blank. Complete allocation after importing animals. Runs use recorded seeds and background execution with cancellation; failures retain the previous result. Review treatment ANOVA on screen and use Repeat with next seed when indicated. Repeating retains the prepared treatment, replicate and housing arrangement. The chosen immutable revision supplies all result tabs and exports. Review validated swaps and physical-pool mappings before applying them.
4. **History:** inspect runs, input revisions, digests and original source files. The source inspector exposes the stored raw cells and formulas without executing workbook code. Activate an existing result with a reason to record that choice.

Animal corrections create an input revision and preserve the original file/sheet/row binding. Previous allocations retain their original inputs. Mapping uses declared physical IDs, complete location strings, capacity and block eligibility; the interface does not invent housing locations.

## Saving and recovery

Save and Save as write schema 1 `.experimentproj` files. Legacy `.poultryproj` files remain readable, and Save prompts for a new-format filename without overwriting the original. The title and status indicate unsaved changes, including unapplied settings or selection drafts. New, Open and Close offer save/discard/cancel when work is dirty. A failed open leaves the current project intact.

Saved projects retain complete history and source evidence. Saving the same file checks its loaded disk digest to prevent overwriting external changes, and atomic replacement retains the preceding `.bak`. Recover backup validates the backup explicitly and requires saving it to a new path. Unsaved work is not autosaved.

## Verification and limits

`./build-and-check.ps1` verifies the engine and controller, builds the Windows application and runs native form smoke checks with a synthetic demonstration study. The controller checks cover dirty state, failed opens, save conflicts, recovery, cancellation, immutable previews and stale result guards. Native checks exercise import validation, changed and rejected previews, stage behavior and font loading. Offscreen renders cover all stages, the 1200-pixel minimum client-width view and import, swap, mapping and correction dialogs. Results are in `analysis/check-results.txt` and `analysis/desktop-validation/check-results.txt`.

Tables page through 500 records at a time; source inspection also pages through 25 columns. All records remain accessible. Header ellipses and horizontal scrolling are used when fields exceed the available width. High-DPI support is enabled; renders validate the current Windows environment, not every monitor or scaling configuration.

The verified build passed 52,136 engine/controller assertions and 30 desktop smoke assertions, with no build warnings or errors.

The original macro workbook remains unchanged. Demonstration fixtures are synthetic, and the original workbook's 864-animal study selection still requires the researcher's explicit decision. Section 6 now supplies Excel/report exports and printable physical mapping lists. An installer and a dedicated factorial treatment builder remain later work. This milestone supports manual ordered treatments and imported treatment/factor metadata.
