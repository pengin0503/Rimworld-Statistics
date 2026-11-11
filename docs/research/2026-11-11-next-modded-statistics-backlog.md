> **Repository migration (2026-11-11):** This audited source research was migrated from the separate statistics-addon repository. All implemented P0 metrics now ship inside `pengin0503/Rimworld-Statistics` in the integrated host PR #103; there are **no separate statistics addons required**. Mentions of addon ZIPs, releases or the old draft PR are historical background, not the current installation procedure. The remaining P1/P2 proposals are still proposals.

> **Implementation status as of 2026-11-11:** the P0 rows for DBH connected tank capacity, pump capacity and groundwater capacity; Recruitment silver spent and *planned* applicants; and Hospitality confirmed successful/forced/voluntary recruitments are implemented on draft PR #4. All remain experimental. Hospitality guest-*assigned* beds are implemented as a separate save snapshot (not physical sleeping occupancy); true currently slept-in guest beds remain proposed. P1 lifetime water flow and P2 ecosystems are not implemented. Past numeric records survive target removal as host profile descriptors and observations; see host `ModMetricPersistenceTests` (7 cases green in addon CI). The existing table is retained as design rationale, not as a statement that all rows are still unimplemented.

# Next optional MOD-specific statistics (2026-11-11)

## Selection principles

Prefer statistics that cannot be supplied by base RimWorld's generic counters or by an existing **ThingDef-based** craft/harvest/trade record. For each statistic, distinguish **confirmed events since activation (lifetime along the recorded game lineage)** from **successful-save snapshots**; snapshots of a changing resource must never be summed across saves to simulate lifetime production. Preserve attribution to colony and optional target version, use bounded event IDs, and record missing targets/unknown readings as unknown, not zero.

Priority reflects user value, ability to audit the source, acquisition cost, and load/save/TPS risk. **Proposed** entries are not currently implemented. A target source may change and each requires a fresh source/version audit. Do not modify the Rimworld-Statistics host merely to hook a specific MOD.

## Baseline available in addon PR #4 (not proposals)

| Target | ID / metric | Behavior |
| --- | --- | --- |
| Hospitality | `mod.orion.hospitality.visitors.guests/groups` | Current guests/groups at successful save (existing) |
| Hospitality | `mod.orion.hospitality.visits.groups_arrived` and `.guests_arrived` | Confirmed new visits and arriving group members since opt-in |
| Recruitment | `mod.pengin0503.recruitment.queue.active` / requestsaddon.started | Current drives and confirmed new drives (existing) |
| Smart Apparel Upgrade | orders.issued | Orders started, **not** actual clothing changes (existing) |
| Dubs Bad Hygiene | `mod.dubwise.dubsbadhygiene.water.piped_stored` | Stored connected-tank water in L at successful save (added by PR #4) |

## P0 — low-cost, high-value candidates

| MOD | Statistic / unit | Capture strategy | Feasibility / caveat |
| --- | --- | --- | --- |
| **Dubs Bad Hygiene** | Piped **tank capacity** (L) | Save snapshot; sum each unique tank's `CompProperties_WaterStorage.WaterStorageCap` | Same verified tank inventory and source as stored water. Must confirm property fallback/zero and exclude unconnected tanks |
| **Dubs Bad Hygiene** | Connected **pump throughput capacity** (L/day) | Save snapshot of deduplicated `PlumbingNet.PumpingCapacitySum` | Confirmed IL and target tower UI label `PipedPumpCapacity`. Capacity is **not actual water produced** |
| **Dubs Bad Hygiene** | Connected **well/groundwater capacity** (L/day) | Same `PlumbingNet.GroundWaterCapacitySum` | Source-proven. Do not add the two capacities together: the effective pumping limit is constrained by both |
| **Rimworld-Recruitment** | Silver spent on confirmed recruitment requests (silver) | Event Sum after successful `AddDrive`/payment, durable drive ID | Source exposes persisted `paidSilver` and monotonically increasing `id`. Count once, avoid spending claims when payment fails |
| **Rimworld-Recruitment** | Total applicants expected/received from new recruitment drives (persons) | Event Sum from confirmed `AddDrive` and later outcome event if applicable | `applicantCount` exists, but **generated applicants ≠ new colonists**; identify state semantics separately |
| **Hospitality** | Guest recruitments, including voluntary vs forced (persons) | Confirmed successful `GuestUtility.Recruit` hook, separate series by outcome | Source exposes `forced`; confirm after-effects and dedup across pawn transfers, not on click |
| **Hospitality** | Current occupied guest beds (beds) | Save snapshot (home maps only), audited `Building_GuestBed` ownership/occupancy | New Hospitality-specific bed state rather than vanilla bed counts; save-only scans can be bounded |

## P1 — cumulative features requiring stronger contracts

| MOD | Statistic / unit | Capture strategy | Principal risk |
| --- | --- | --- | --- |
| **Dubs Bad Hygiene** | **Actually consumed** piped water (L) | Successful `PlumbingNet.PullWater` transfers, batched into durable periodic counters | Many call sites from showers/toilets/pools/washing; per-call SDK hashing is too expensive, and some transfers may be internal |
| **Dubs Bad Hygiene** | **Actually pumped/produced** clean water (L) | Audit `CompWaterStorage.CompTick` → `PushWater` accepted volume; persisted interval batch | `WaterPumpedPerTick` is capacity-derived; returned remainder, recycling and tank cross-transfers need attribution |
| **Dubs Bad Hygiene** | Sewage handled/treated (L) | Source-audited sewage handler/treatment successful transfer events | Need unit proof and careful separation of generated vs recycled vs discharged amounts |
| **Dubs Bad Hygiene** | Completed showers/baths/toilet uses (visits) | Successful job/fixture completion, not per-tick water pulls | Multi-tick jobs and interrupted use must not overcount |
| **Hospitality** | Completed guest stays and guests departed (groups/persons) | Audited group end/departure callback with outcome | Departure, death, recruitment, repatriation and early dismissal differ |
| **Hospitality** | Trade volume with guests (silver) | Source-verified completed guest sale/purchase transactions | Exclude ordinary trade, gifts, cancellations and double-counted game trade metrics |
| **Smart Apparel Upgrade** | Successfully completed automatic apparel swaps (items/jobs) | Confirm new equipment actually worn, correlate with an issued automatic order | Current addon counts orders **issued**, not success; job failures, interrupted changes, and alternative managers |
| **Rental Warehouse** | Items successfully stored/retrieved, rental charges (units/silver) | Own MOD's confirmed storage transactions and payments | Verify source definitions and permanent vs returned stock; don't sum pending moves |

## P2 — further MOD families (source research first)

| MOD/system | Possible non-vanilla metrics | Prerequisite |
| --- | --- | --- |
| **Rimefeller** | Crude oil actually pumped, refined chemfuel produced, chemical spills | Installed target/source revision, unit semantics, completed production transactions |
| **Rimatomics** | Nuclear fuel consumed, waste produced, reactor incidents | Source audit and outcome APIs; distinguish electrical capacity from actual energy |
| **GlitterNet** | Unique network jobs successfully processed, computing/power usage | Verify installed version's actual network/API and whether these values are already exposed |
| **Hospitality companions / guest-service extensions** | Guest service orders completed, special accommodation types | Explicit dependencies and no cross-provider double counting |
| **Vanilla Expanded and other content MODs** | Unique system events (fishing catches, special factories, ideology/campaign-specific mechanics) | First check whether generic host ThingDef metrics already cover item craft/harvest/sale; add only genuinely missing mechanics |
| **RimTalk / social AI integrations** | Successful in-game conversation events, not model API requests | Reliable committed gameplay event, no private text and no extra external requests |

## Recommended next sequence

1. Complete real-game source UI parity and no-host/no-target tests of DBH piped storage and Hospitality arrival events. Until then mark Experimental, not Verified.
2. Add low-overhead **DBH tank capacity, pump capacity and groundwater capacity** if the exact 1.7 value definitions and unique-network ownership remain valid. Label L vs L/day precisely.
3. Implement **Recruitment silver spent**, then Hospitality successful recruits, with a source-pinned event contract and SDK replay tests.
4. Design a **replay-safe periodic water transfer accumulator** before adding any lifetime DBH production/consumption metric; never emit one SDK event per game tick or reconstruct totals by subtracting save snapshots.
5. After SDK support evolves, consider explicit `Liters` and `LitersPerDay` units in the **generic host** (a separate backwards-compatible host PR) rather than overloading the legacy numeric `Count` formatter.

### Cross-colony presentation

For each stable metric, the host already provides per-colony and aggregate numeric readings when enabled. Automatic generic max/min-colony leaderboard rows for arbitrary SDK keys are **not guaranteed by this addon**; investigate a separate host-level generic ranking enhancement rather than promising it here. The aggregate of each colony's representative snapshot is **not** one real-time view of all currently loaded saves. Store number and semantic provenance with each reading.

### Safety/compatibility

No direct dependency DLL redistribution; each addon remains separately installable, fail-closed on missing signatures/version/host, preserves recorded profile metadata on removal, and does not infer events that occurred before the addon was enabled. External MOD source is a read-only reference; Cloudflare R2 contains game references only and must never be changed by this work.
