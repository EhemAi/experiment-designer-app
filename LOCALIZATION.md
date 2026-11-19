# Application languages

The existing Windows app supports `en`, `es-ES` and `es-MX`. The installer sets the initial application preference after a successful installation. The Settings dialog persists later choices in `%LOCALAPPDATA%/ExperimentDesigner/language.json`.

`AppText` loads embedded English-keyed JSON catalogs from AppCore. Mexican Spanish overrides shared Spanish only where regional wording differs. Add new interface text to the shared catalog and regional overrides where appropriate. Preserve all numbered substitution fields. Dynamic semantic substitutions belong in the explicit `AppText.Argument` whitelist; researcher values must remain literal.

WinForms display translation is implemented by `UiLanguage`, localized labels, dropdown painting and grid headers. Canonical control text, enum values, grid binding keys, identifiers and project serialization remain language independent. Text boxes retain researcher input. Raw source columns and imported observations must never be translated.

`ReportLanguage` translates copied report metadata and known status fields before Excel, HTML and native printing. Researcher project names, animal/treatment identifiers, labels, notes and swap reasons stay exact. Numeric Excel cells stay numeric. Prepared layouts are selected before translating table names.

Offline guides are `User guide.html`, `User guide.es-ES.html` and `User guide.es-MX.html`. Keep all three in the package. The MIT license remains the original English legal text. Decimal input uses a point; native Windows file/print interfaces can follow Windows display language.

## Verification

Run `check-source.ps1` and `build-and-check.ps1`. Run the desktop with `--localization-check <source-root>` for three-language workflow/dialog rendering, minimum-size layouts, dirty-draft preservation, Spanish sex import, preference fallback, project round trips and Excel/HTML/native report verification. Generated test studies and renders go under `analysis/localization-validation`.

`build-package.ps1` also runs language checks against the published app using only the bundled runtime. Setup wizard checks cover all three languages, four programmatic scales, cancellation/retry, language handoff, shortcut choices and uninstall preservation. These are local preview checks; physical monitor transitions and professional human linguistic review are separate activities.
