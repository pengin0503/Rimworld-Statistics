# Changelog

This file starts at v2.1.0. Earlier releases are recorded under `docs/releases/`.

## [2.1.0]

- Centralized save-version gating, finer partial-record reasons, one automatic retry when a save changes during reading, and tick/time anomaly guards.
- Protected pre-restore backups from retention, added a restore diff preview, backup notes, a 2 GiB ZIP dry-run guard, retention limits and file-lock retries.
- Added profile capacity and external-change warnings, stopped/failed provider diagnostics, tab badges and guided recovery from the newest valid backup.
- Reworked import flow with save-name ignore patterns, present/archived counts, added/updated summaries, single-save recheck, per-failure actions and title-screen progress.
- Improved settings, coverage and endings screens: search, collapsing, recommended selection, direct recording toggles, type filters, `?` guide, two-stage tooltips and empty-state links.
- Persisted Details/Rankings view state and added source filtering, display formats, hide-zero, shared AND/exclude search, formula popover, rate units and expand/collapse all.
- Search now also matches English names, DefNames/keys and MOD package names (plus resolved MOD names) alongside localized labels in Details, Rankings, pickers, settings, Endings and Chronicle.
- Improved pickers, dialogs and charts: recent items, inline naming with reset, current-colony marker, zoom presets, multi-select chronicles, interval and bar-chart controls, unified confirmations.
- Kept Excel-ready BOM CSV exports and added export-folder controls, Markdown/TSV copy, stable `RWS-E1xxx` import error codes, a one-time What's-new dialog and new CI consistency checks.
