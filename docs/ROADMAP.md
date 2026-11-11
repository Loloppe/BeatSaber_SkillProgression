# Roadmap

| Phase | Status |
|-------|--------|
| 1 — Research & discovery | **Done 2026-11-10** (findings in TECH_FINDINGS.md, API_INTEGRATIONS.md) |
| 2 — Product definition | **Core concept decided** (D-003..D-012); milestones/screens/platform panel deferred until model evidence |
| 3 — Architecture | **In progress** — repo layout done (D-014); next: Core domain model + model.json contract |
| 4 — Technical foundation (project, Reactive PoC, navigation) | **Done** (shell, D-016, D-024) |
| 5 — Data integration (BL → AccSaber → SS) | **Done for MVP**: BL scores (cache + incremental), BL/SS/AccSaber platform stats; SS/AccSaber pools later (D-023) |
| 6 — Progression engine + milestones + history | **Engine done** (Core, 83 tests, anchors 11/11); achievements + history **done** (D-033..D-035, in-game review rounds 2026-11-10) |
| 7 — UX screens | **MVP screens done**: Training, Platforms, Explore (2 in-game review rounds) |
| 8 — Testing & refinement | Not started |

## Session log
- 2026-11-10: Phase 1 investigation. Existing repo = untouched legacy BSMT template (gameVersion 1.18.3).
  User decided target = 1.40.8. Open questions recorded in DECISIONS.md. Next: discuss Q-001.

- 2026-11-10 (cont.): User decisions D-002..D-006 (Reactive v2, hybrid model, population-driven archetypes,
  snapshot/fresh start, self-contained mode). Second research round: Reactive v2 vs BL v1 conflict, population
  data (portaBLe DB + attempts), Analyzer map features, direct level start on 1.40.8. Draft in PROGRESSION_DESIGN.md.
  Next: Q-010 (population data source), then Q-003 (Reactive v2 distribution).
- 2026-11-10 (cont.): D-007..D-012 decided (data sources, path selection, layered waypoints, fresh start,
  local play log, parallel model-led build order). Background: assessing BL `reactive2` port to 1.40.8.
  Remaining Phase 2 topics deferred until model track gives evidence: milestone catalogue, screen structure,
  platform panel contents. Next: pipeline language/tooling (Q-012).
- 2026-11-10 (cont.): D-013 (Python training / C# Core), D-014 (repo layout). git initialised, legacy template
  deleted, solution scaffolded (Core, Data, Tools, 2 test projects; builds). BL reactive2→1.40.8 patch saved in
  `tools/dev-env/`. Next steps: (1) Core domain model + model.json contract; (2) `extract` command over portaBLe
  Database.db; (3) set up separate 1.40.8 dev instance with BL-reactive2 + Reactive v2 (needs user go-ahead).

## Proposed MVP (OUTDATED — written before D-004/D-006; to be redefined in Phase 2)
1. BeatLeader-only data: auto-detect player id, PB score sync (full + incremental), BL ranked pool.
2. Three skill tracks (Pass, Acc, Tech) with tiered ladders from BL ratings; current tier + progress.
3. "Next objectives": 3–5 stretch/improvement recommendations per track, exported as a playlist.
4. A handful of milestone types; local snapshot history.
5. One Reactive screen (dashboard) + a recommendations list, reached via a main-menu button.
- 2026-11-10 (cont.): D-015 (population DB = Prod Autoreweighted), D-016 (dev env in existing 1.40.8 instance,
  installed + backed up). User FPFC smoke test OK (BeatLeader UI on Reactive v2); score submission untested.
  Attempts export measured: 2022-03 → 2026-10-07, readable from Python directly.
- 2026-11-10 (cont.): Model track explorations 01–05 (training/explore/, findings in PROGRESSION_DESIGN.md).
  Decisions D-017..D-023: two-stage features, expert-defined destinations (Accuracy, Midspeed, High Tech, Speed,
  Extreme, Unspecialised) on pass/tech ratings, 11 user anchor players all correct, waypoints ordered by path pp with
  push/polish components, map-pool focus after MVP.

## Status of the two tracks (D-012)
- **Track 1 (model):** destinations ✔ (rules v6), divergence ✔, waypoints v1 ✔, leaning ✔ (06), objectives +
  back-test ✔ (07). **Stage 2 (D-017) done 2026-11-10:** Core port (profile, rules, leaning, paths, waypoints,
  push/polish objectives, recommender, engine) with 44 tests; Data (BeatLeader client, model loader) with 9 tests;
  `training/build_model.py` → `data/model/model.json.gz` (3,894 maps, 1.8 MB gz); Tools `player` / `check-anchors`.
  **check-anchors: 11/11 user anchors correct end-to-end from the live BL API.** Fixed on the way: pp curves now
  include pp-neutral modifiers (BE/PM/GN/DA) and a finer accuracy grid above 94%.
  Next: user review of `player` reports; progress-to-next by profile similarity (D-009) instead of path-pp share;
  waypoint D not always monotonic (e.g. High Tech wp 8→9) — smooth waypoints; more low-level training history.
- **Track 2 (plugin shell):** dev instance ready (D-016). **Shell built 2026-11-10** (`src/SkillProgression.Plugin`):
  BSML menu button → self-contained FlowCoordinator (no side screens, back button hidden, own Exit), Reactive v2 view
  (state bindings), direct start of a hard-coded test map ("Your Best Friend" E+), BL recording hook via reflection,
  local stats update like SoloFreePlay, PlayRecord appended to `UserData/SkillProgression/playlog.jsonl`.
  Builds and deploys to the dev instance. **User test 2026-11-10 (FPFC): menu button, takeover, direct start,
  return after clear/quit/fail, Exit all OK; play log records correct.** BeatLeader upload confirmed in VR (2026-11-10).
  Follow-ups: gameplay setup panel added and confirmed working (D-024); back arrow replaces Exit; mark NF clears at 0 energy as soft fails in PlayRecord;
  pause count not recorded yet.
- 2026-11-10 (cont.): Review rounds 1–3 with the user (D-027 always a next step, D-028 style mix + rank-milestone
  waypoints), UX rules D-029/D-030. **First integrated plugin build:** Training screen (start-mode choice with
  confirmation, style mix, focus-style selector, waypoint/progress, short-term steps, Next up with Play/Get & Play and
  Skip, result line), Platforms panel (BL/SS/AccSaber live), BeatLeader score cache + incremental sync, local play log
  merged as evidence, BeatSaver download on demand. Model loaded from UserData/SkillProgression/model.json.gz (dev copy
  by the build). **Awaiting in-game test.** Model hosting for players still to decide.

## Status 2026-11-10 (end of MVP build)
Done: model pipeline (explorations 01–07, build_model.py), Core engine (style mix, destinations, leaning, milestones/
waypoints, push/polish objectives, short-term steps, Next up with skips and 3-miss rule, starter maps, Explore), Data
(BL client/cache, platform stats, state, play log), plugin (self-contained flow, gameplay setup panel, Training,
Platforms, Explore, direct start, BeatSaver download, BL recording hook).
Outstanding / known gaps:
- ~~Verify a BeatLeader upload from a plugin-started clear~~ — confirmed by the user in VR 2026-11-10.
- ~~Milestones & achievements and progression history~~ — done 2026-11-10 (D-033..D-035, reviewed in-game).
- ~~Progress-to-next by profile similarity (D-009)~~ — done 2026-11-10 (D-039).
- ~~Fresh-start calibration session (D-010)~~ — done 2026-11-10 (D-040), awaiting in-game test.
- **Release (D-043):** the user ships a GitHub release .zip (plugin + libs + bundled model) once Reactive v2 is public
  with a matching BeatLeader build — nothing to build for hosting. ~~No-fail soft-fail flag and pause count~~ — done (D-036).
- 2026-11-11: launch refuses maps with missing required mods (SongCore check); download waits for a running SongCore
  load; "Downloading" button reset on errors. Explore "Mods" filter (D-044) added then removed (D-045).
  Curated maps in Explore (D-045): pipeline, Core, unranked BL import, Explore pool entry — awaiting in-game test.
  One shipped DLL (D-046): Core/Data compiled into the plugin — verified in-game by the user 2026-11-11.
  Mapper names in map rows; Explore centred on Training's difficulty (D-047) — awaiting in-game test.
  Version 1.0.0: a Release build now writes the complete zip (bin/Release/net48/zip: Plugins/ dll+pdb + UserData
  model, via the CopyModelToArtifact target before BSMT's zip step); not published — still gated on public Reactive v2 + matching BeatLeader build (D-043).
  Clipped long titles; resync on plugin open; Solo tab for Explore lists (Improve / Never played) with Solo plays as
  scores, after-play downloads and recommended-difficulty preselect (D-049) — verified in-game by the user 2026-11-11.
- ~~SS/AccSaber map pools (D-023)~~ — done 2026-11-10 (D-041). Postponed: unranked maps in Explore (D-032, D-043: catalogue-wide BL fetch too long). ~~adaptive step sizes~~ done 2026-11-10 (D-042).
