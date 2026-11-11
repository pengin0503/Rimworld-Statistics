# Integrate Optional Statistics Into Main — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or test-driven-development to implement task by task.

**Goal:** Ship all four optional gameplay-MOD statistic integrations in one Rimworld-Statistics package while preserving every recorded statistic and eliminating installation of separate addons.

**Architecture:** Reuse the installed SDK host and its provider descriptor store. Move source-audited integration classes into the main assembly, register immediately after host initialization, and keep each gameplay source independent and fail-closed.

**Tech Stack:** RimWorld 1.7, C#/.NET 10, Harmony, SDK 1.1, GitHub Actions.

**Spec:** docs/superpowers/specs/2026-11-11-integrated-optional-mod-statistics.md

## Required stages
- [ ] RED: add pure tests (DBH, Hospitality, Recruitment, SAU) and production package invariants; confirm missing implementation fails in GitHub CI.
- [ ] GREEN: port pure logic, source readers/hooks/SDK bridges unchanged where possible; no additional Mod-derived classes.
- [ ] Add IntegratedModStartup, install after host SDK and before the registry seal; separate target guards and installed-legacy-addon coexistence.
- [ ] Move Japanese/English translation keys without overwriting existing keys or adding duplicates.
- [ ] Import or duplicate pinned public source/ABI contract checks as CI gates; keep third-party DLLs out of shipping ZIP.
- [ ] CI: compile, run full Core tests, new regression tests, translation/package checks; inspect failures and repair.
- [ ] Self-review actual main PR diff and metadata/history continuity, merge when CI green; leave historical test/CI evidence.
- [ ] Retire old draft addon PR and mark standalone repository deprecated after successful host merge. Repository deletion itself requires owner's GitHub administration UI because not exposed by connector.

**Review risks:** Source assembly load order; optional target absent and user missing mods; duplicate hooks with old addon left installed; host SDK adapter seal timing; saved stable metric keys with old provider version; Japanese/English key duplication; large-game TPS and load/reload event replay.
