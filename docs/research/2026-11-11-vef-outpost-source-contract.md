# GEN-OUT-001: VEF Outposts source-contract audit (2026-11-11)

Pinned read-only upstream: [Outpost.cs](https://github.com/Vanilla-Expanded/VanillaExpandedFramework/blob/e7fab7dfb352adaf614fe9358897007e1b48d26e/Source/Outposts/Outpost/Outpost.cs) and [Outpost_Deliveries.cs](https://github.com/Vanilla-Expanded/VanillaExpandedFramework/blob/e7fab7dfb352adaf614fe9358897007e1b48d26e/Source/Outposts/Outpost/Outpost_Deliveries.cs); About.xml includes RimWorld 1.7 and package ID OskarPotocki.VanillaFactionsExpanded.Core.

`TickInterval` → `Produce` → `Deliver(ProducedThings())`; `Deliver` evaluates `items.ToList()` once. Transport modes: Teleport, PackAnimal, Store, ForcePods, PackOrPods; absent map stores goods. `GenPlace.TryPlaceThing` outcome is ignored by Teleport, and pack-animal `TryAddItemNotForSale` outcome is ignored, so neither producing nor dispatching proves receiving. There is **no safe success point for a generic item total**, and this change does not patch, double-enumerate or claim production/delivery quantities.

Fallback `exploration.outposts.active`: successful-save-only `SavedStateMetricReader` counts unique player-owned world objects in the `Outposts.Outpost` type hierarchy. The opt-in `outpost_state` snapshot acquisition is unavailable if no enabled Outpost WorldObjectDef exists. Cross-colony sum, highest and lowest colony use representative save snapshots, never a lifetime sum of all saved moments. Historical values remain in profiles after removal. No external VEF assembly is referenced.

Status: source-pinned static audit and CI; in-game RimWorld 1.7 / VEF verification not yet complete. No automated release or Workshop upload.
