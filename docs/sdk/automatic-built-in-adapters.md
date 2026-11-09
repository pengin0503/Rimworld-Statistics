# Built-in automatic MOD adapters (opt-in-free)

This change introduces *source-audited, bundled* adapters for third-party MODs. The other MOD need not implement the Statistics SDK, modify its source, or depend on Rimworld-Statistics. Statistics will begin recording **from the first successful game save after installation**, when a supported adapter and its owning MOD are active. Historical values remain available if a MOD is later removed.

## Support and limitations

- **Hospitality / Hospitality (Continued)**: package ID `Orion.Hospitality`, current visiting guests and active visitor groups, across loaded player home maps. Both are **save-time snapshots**, not the number of guests who have ever stayed. These values remain experimental and are tagged accordingly in the Details UI.
- **Rimworld-Recruitment**: existing requests started and active recruitment drives. Requires the source-audited version range `1.1.2 <= version < 1.2.0` and exact runtime binding signatures. An unknown version or a changed signature causes the adapter to fail closed.

Hospitality (Continued) 1.7 source audit: [Zaljerem/Hospitality](https://github.com/Zaljerem/Hospitality), `About/About.xml` (`Orion.Hospitality`) and `Source/Hospitality_MapComponent.cs` (public `PresentGuests` and `PresentLords` collections). Rimworld-Statistics inspects the contract at startup, reads only collection `Count` from each home map at a successful save, and does not patch Hospitality. If the target assembly or collection shape is incompatible, the adapter fails without inventing data.

## Why this is not universal automatic gameplay telemetry

An arbitrary MOD cannot expose semantic metrics (e.g., a lifetime guest tally) solely through its package ID. Such numbers require either a source-audited adapter in Rimworld-Statistics or the MOD's own SDK integration. Never inspect arbitrary fields or invoke unknown hooks and label them verified numbers: their meaning, game-thread safety, performance and save/load semantics would be unproven.

External SDK consumers **remain opt-in**: untrusted third-party registrations do not automatically enable themselves. Bundled, source-audited adapters are the only exception. Built-in adapters become visible in Details independent of the `Show Recorded Experimental` checkbox; they still show **experimental evidence** and do not imply game acceptance. Existing profiles and on-disk formats are unchanged.

A static build/CI green result does not establish compatibility with any installed Hospitality version or correctness during real play. Runtime verification is pending.
