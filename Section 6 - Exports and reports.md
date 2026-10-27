# Section 6 — Exports and reports

The application exports the chosen completed allocation as Excel or printable HTML and supplies native print preview and printing. All outputs read a captured immutable revision, including its own settings, selected/excluded animals, edits and original source bindings. Exports neither change the active result nor save pending project changes.

## Use

Launch `./run-designer.ps1`. Open a saved project or complete an allocation, go to Allocations and choose the desired result revision. Click **Export / print chosen revision**.

Choose movement order: treatment/unit, old location or new location. Export Excel or printable HTML to a new file. Use **Print preview** to inspect the native paginated report. Use **Print / PDF** and select Microsoft Print to PDF in the Windows dialog to save a PDF, or select a physical printer. Printable HTML also offers **Print / Save as PDF** through the browser. Printer-dependent availability/errors are reported; HTML remains available when no printer is configured.

Historical results and results based on earlier inputs carry explicit labels. Unmapped allocated animals say Unmapped. Excluded animals carry ResearcherExcluded and have no invented destination. Unresolved selections and failed runs cannot become completed report output.

## Excel content

The macro-free `.xlsx` workbook includes:

- Summary: study settings, project/result/input IDs, report snapshot UTC, input revision UTC, digests, design rule, seeds, algorithm version, counts, acceptance settings, overall BW and metric definitions.
- Movement: every source-pool animal, cohort, BW in grams, old location, disposition, treatment, replicate/block, physical destination and immutable record ID.
- Unit BW: counts, total and mean BW, sample SD within each pen/cage and physical location.
- Treatment balance: descriptive statistics across experimental unit means, including SD, CV and differences from the overall selected mean. No outcome analysis or significance test is added.
- Blocks and attempts: current edited block CV separately from original run attempts and original best/last CV.
- Physical map: the complete declared pool, scoped locations, assigned treatment/replicate, capacity, block eligibility and exact mapping seed. Existing mapping rules require exactly one physical unit per allocated unit. The report is a location list, not an invented floor plan.
- Swap log: ordered reasons, record IDs and before/after treatment/replicate/block slots.
- Animal provenance: include/exclude decisions and reasons, file/table/row bindings, ancestry and notes for every pool record.
- Sources and Import mappings: original hashes, capture UTC, exact ranges, units, column mappings and integer-ID policy.
- Treatments: ordered IDs and labels, plus original imported factor values and source bindings when present.

Data tables have a single header row, filters and frozen header/identity columns. BW/statistical numbers remain typed numeric values with a three-decimal display format. Counts are integers. IDs, hashes and seeds are explicit text, preserving leading zeros and integers beyond Excel's numeric precision. User strings beginning with `=` remain literal strings and cannot become formulas. Excel supports about 15 significant digits; the project retains exact decimal inputs and replay evidence. Editing exported cells does not recalculate the allocation.

The workbook specifies Manrope. Excel uses the available font on the destination computer because XLSX does not embed this application's font files. Native print and self-contained HTML use the bundled Manrope/Poppins fonts. Font data and OFL licenses are included in the HTML report, so it needs no network connection.

## Printable reports

Reports use landscape Letter pages, readable professional type, light body tables, charcoal headers and restrained teal accents. They include the study summary, complete movement list, unit BW, treatment balance, block diagnostics, physical map, swaps and source hashes. The full machine/audit columns remain in Excel and the project.

Native pagination repeats section/column headings and page numbers. It keeps ordinary rows together and continues exceptionally tall rows across pages without dropping text. Printed numeric values are rounded to three decimal places. Native printing draws vector text using the same layout as the offscreen verification pages. Browser printing applies page-break and repeating-header rules; final pagination depends on browser/printer settings.

Exports use same-directory temporary files and replacement only after a complete successful write. Failed writes retain the previous output and remove temporary files. The UI requires the save dialog's overwrite choice and prevents exports overwriting the chosen project's original sources or project file. Excel row, column and text limits are checked explicitly; unsupported content is rejected, never silently truncated.

## Verification

`./build-and-check.ps1` runs the export checks alongside existing engine/controller checks, builds the desktop application and renders native pages. Section 6 tests verify reconciliation, all rows/provenance, maximum unsigned seeds, leading-zero long IDs, formula-like text, macro-free structure, frozen panes/filters, HTML escaping, immutable historical weights and failure preservation. A synthetic 900-animal pool verifies 864 allocations and 36 explicit exclusions beyond UI page boundaries. Desktop tests verify the visible export action, 576 printed movement rows and lossless continuation of a 10,000-character cell.

Saved XLSX fixtures were independently imported and all eleven sheets rendered with the bundled spreadsheet artifact tool. Native pages were visually reviewed. Results and fixtures are under `analysis/report-validation` and `analysis/desktop-validation`; the combined runner output is `analysis/check-results.txt`. The checks dispatch no printer jobs. A physical printer job, interactive Excel opening and Microsoft Print to PDF save remain dependent on the user's installed applications and printer configuration; they are not claimed as automated checks.

The original macro workbook is unchanged. All generated verification studies are demonstrations. Distribution packaging, final researcher workflow acceptance and the user guide remain the next main work.
