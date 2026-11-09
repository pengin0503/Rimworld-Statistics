# Private archive read-only acceptance for historical recovery (2026-11-09)

This optional Core test is a **measurement tool**, not an authorization to mark experimental values Verified. The public repository and CI contain no private saves.

## Windows local invocation

1. Use a disposable, **read-only copy** of the desired save folder when possible. Do not put the folder or save files under the public GitHub repository.
2. Install .NET SDK 10 and run PowerShell from the Rimworld-Statistics repository root:

```powershell
$env:RWS_PRIVATE_SAVES_DIR = "D:\RimWorld-test-saves"
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true --filter "FullyQualifiedName~PrivateHistoricalBackfillAcceptanceTests" --logger "console;verbosity=normal"
Remove-Item Env:\RWS_PRIVATE_SAVES_DIR
```

The path is an example; replace it with the local test-copy directory. Do not share the full console output if it contains a custom path. The test suppresses original save names and personally identifying pawn/campaign/MOD information in its own diagnostic message.

## Checks

- Detect .rws and .rws.old files with the **same filter** as the production importer.
- Import into an isolated temporary profile; the original directory is opened read-only.
- Run a second unchanged scan; require zero re-reads and no failed reads.
- Check file sizes/mtime did not change.
- Check any recovered faction outputs are marked `Partial`/`Experimental`; unknown/absent data is not converted to zero.
- Verify any conditional intervals are marked Partial and count only their number, never additive totals.
- Report aggregate first/second scan time, file counts, partial faction count and interval count, **without reporting names, map data, saved MOD lists or original file hashes**.

## What this does NOT prove

The optional test cannot independently verify the true faction counts, equality to the live in-game capture, save duration overhead, real TPS or the game's exact 1.7 XML serialization without a matching runtime. It is skipped in public CI when `RWS_PRIVATE_SAVES_DIR` is absent.

The historical archive remains Unverified. Results can be compared against a controlled game session or independently reviewed source XML *in the owner's private environment*, and only then used to update [Issue #71](https://github.com/pengin0503/Rimworld-Statistics/issues/71) or [Issue #72](https://github.com/pengin0503/Rimworld-Statistics/issues/72). Do not commit source saves, raw logs, game paths or mod manifests.
