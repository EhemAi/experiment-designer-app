# Section 3 — Allocation engine

October 3, 2026. Implemented engine version: `1.0.0-splitmix64`.

## What is implemented

The engine follows Section 2 and the researcher's latest instruction removing sex allotment. It uses immutable records and explicit researcher-selected animal IDs. It does not inspect the VBA or run Excel during allocation.

All three methods produce exactly T × R pens containing P animals each. Treatment IDs and labels are unique text; leading zeros survive. Animal record IDs are unique, and displayed animal identity is unique within its cohort unless the entire study is explicitly marked demonstration data. The engine accepts an expanded treatment list; building a factorial treatment table belongs to the interface section.

BW is represented as decimal grams. Zero, negative and out-of-range values are rejected. Decimal inputs cannot represent NaN or infinity. Import parsing and unit conversion will validate incoming spreadsheet values in Section 4. The engine supports up to 100,000 pool records and 100,000 selected animals; BW is capped at 1,000,000 grams as a numerical safeguard. This cap is an implementation bound, not an experimental selection criterion. Requests outside these bounds produce validation errors rather than silently truncating data. Count arithmetic is checked for overflow.

## Allocation methods

**RCBD:** Canonicalize selected animals by ordinal internal ID, shuffle to break BW ties reproducibly, and stably sort BW descending. Each block has T × P animals and P consecutive subblocks of T. Independently shuffle treatment order within each subblock. Retry each block within its fixed membership, accepting its first passing candidate. Every attempt, including a single attempt, obeys an enabled CV limit.

**CRD:** Shuffle the exact selected pool and fill balanced treatment/replicate pen slots. Replicate numbers label units within treatments; they do not create BW blocks. RCBD balance filtering is rejected for CRD.

**Conventional:** Make one treatment-order permutation. Apply its forward and reversed order alternately across consecutive BW subblocks throughout the selected pool. Alternation continues across block boundaries, including when animals per pen is odd. The method runs once and reports descriptive block CV; retry filtering is unavailable.

No method silently chooses a subset, replaces animals or considers sex. Unselected animals remain researcher-excluded in movement views.

## Reproducibility contract

The implementation uses SplitMix64 and unbiased rejection sampling for Fisher–Yates indices. The checks include known generator output vectors and a pinned allocation vector. It does not use `System.Random`, time-based seeds, process hash codes or Excel RAND.

Streams are derived from SHA-256 over a versioned domain and the recorded seed. BW tie order, animal assignment and physical pen mapping have separate domains. Physical mapping takes a separately recorded seed. The stream implementation uses little-endian seed extraction and ordinal record ordering.

The allocation fingerprint includes engine version, design, replication, pen size, filtering settings, demonstration flag, ordered treatments and canonical selected animal identities/BW. It excludes input row order, old-location annotations and unselected animal BW because those do not govern allocation. Section 4 will additionally preserve the full source pool and project revisions for provenance; the allocation fingerprint is not a substitute for a complete project record.

Replay guarantees apply to the implemented algorithm version and equivalent canonical inputs. Changing the algorithm requires a new version and updated pinned fixtures. The new software preserves the agreed mathematical rules and constraints, not Excel's particular random draws.

## Balance and acceptance

Each pen reports count, decimal total/mean BW and sample SD. SD is unavailable for a one-animal pen. Each blocked design reports sample CV among its treatment pen means. Common P makes CV of pen totals equivalent; computing CV from decimal totals avoids rounding away very small differences when dividing by P. Variance calculations then use double precision.

The comparison is unrounded CV ≤ threshold. A zero threshold requires exactly zero computed CV, with equal totals handled explicitly. Positive thresholds permit a fixed maximum of eight adjacent representable double values above their conversion boundary. This is a representation tolerance, not display rounding or a scientific allowance. A below-boundary test and a tiny-positive-CV/zero-limit test verify rejection.

Treatment diagnostics report pen-level replication, treatment mean BW, min/max pen means, sample SD/CV among pen means, and signed absolute/relative differences from the overall mean. They introduce no additional acceptance conditions and no inferential claims.

On exhaustion the result records the failing block, actual attempt count, best CV and last CV. Earlier passing block traces are retained, but partial assignments are not returned as a completed result. Cancellation before a run or during attempts also returns no completed candidate.

## Physical locations, edits and output consistency

Physical pens require unique IDs and unique complete scoped location strings, sufficient capacity and exactly the required number of units. RCBD requires a complete eligible physical pool for each block; CRD uses the global pool. Mapping is independent of animal allocation and preserves its seed and assignments.

A swap exchanges two allocated records. It preserves pen capacities, identities and treatment replication. Cross-block swaps are supported. Enabled CV limits are recalculated before returning the candidate; balance-breaking swaps fail. Both original block and subblock stay with each animal as provenance.

Every edit records its reason and both animals' before/after assignments. The complete immutable edit sequence accompanies the edited revision. A reversal creates another edit instead of erasing history. Seed plus edit sequence replay is checked. A pure operation can be used for preview without committing it to a session.

The session commits only complete successful runs and successful edit/map operations. Invalid input, exhausted attempts, cancellation and rejected edits retain the previous active result. Successful runs and revisions remain in in-memory history. Persistence and crash recovery are Section 4 work.

Movement records distinguish allocated, researcher-excluded and selected-unallocated animals. They join by immutable IDs, not BW or concatenated keys. Mapped movement rows use the current mapping. Balance and movement output use the allocation's corresponding study version; changed BW cannot silently alter a historical report.

## Verification

The dependency-free C# check executable runs 900 combinations across three methods, three pen sizes and 100 seeds. It tests counts, unique membership, replication, BW rank/block/subblock construction, global serpentine alternation, exact seed replay and input-row-order independence. Independent calculations verify pen totals, means, sample SD and block CV.

Additional checks cover invalid/overflowing settings, unknown/duplicate IDs, cohort identity, demonstration data, explicit exclusions, tied BW, zero and inclusive CV boundaries, single-attempt enforcement, exhausted and successful retries, cancellation, active-result retention, stale inputs, cross-block swaps, balance-breaking swap rejection, provenance, undo/edit replay, physical eligibility/capacity, corrupt cached metrics, movement disposition and treatment-level diagnostics. Public engine types are checked for absence of sex properties.

Five captured native Excel scenarios provide reference calculations: RCBD without sex, CRD without sex, conventional without sex, exact-sized RCBD pool and tied-BW RCBD. Those comparisons use the native assignments to verify native pen means/CVs, then independently exercise the new allocator. They do not assert identical random treatment draws.

The runner also checks a synthetic 6-treatment × 12-replicate × 12-bird study with 0.5% filtering and 30 attempts per block, and a 100,000-animal resource-limit study. Timing includes different work for each: the 864-bird timer measures allocation; the large-study timer also includes independent verification. These are local observations, not product performance guarantees.

The final check run passed **51,956 assertions**. The synthetic 864-bird allocation took 3 ms; the 100,000-animal run plus independent verification took 1,724 ms on this machine. Exact output is saved in `analysis/engine-validation/check-results.txt`. The 864-bird test input and result are saved beside it. The original `.xlsm` remains unchanged; its SHA-256 is `3a8b964c0877643bb1b94ea952b1aa055a87f2e0dadb650d74337460a57b1686`.

## Remaining sections

This milestone is a validated engine library, not an installed Windows application. Section 4 now implements versioned project storage and animal/treatment import with explicit selection, duplicate handling and recoverable saved project history; see **Section 4 - Projects and imports.md**. Section 5 will build the Windows interface around these validated operations. Printable maps, Excel exports, randomization reports and packaging follow. No GUI control can bypass the engine's validation or enabled balance limits.
