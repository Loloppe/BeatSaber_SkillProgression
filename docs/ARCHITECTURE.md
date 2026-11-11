# Architecture

Status: layout approved (D-014); Core data flow and model.json contract approved with pp lookup curves (D-026).

## Projects (D-014)
| Project | Target | Responsibility |
|---|---|---|
| `src/SkillProgression.Core` | netstandard2.0 | Pure logic, no I/O, no Unity: map identity, play evidence, profile features, destination rules, leaning, path state, waypoints, gaps/objectives, recommendation ranking, model-file schema. |
| `src/SkillProgression.Data` | netstandard2.0 | I/O: BeatLeader client (player, scores incremental, ratings by hash), model download/cache, local stores (play log, cached scores, progression snapshots), migrations. |
| `src/SkillProgression.Plugin` | net48 | Game/UI only: menu flow (D-006/D-024), level start (Solo), play capture, Reactive v2 screens. Calls Core/Data. |
| `tools/SkillProgression.Tools` | net10 | `extract` (population DB + attempts → feature tables via Core), `build-model` (assemble model.json from Python fit outputs), `player <blId>` (BL API → Core → report), `backtest`. |
| `training/` | Python | Exploration (stage 1, D-017) and fitting on Core-extracted tables (stage 2). |

## Core data flow (proposal)
```
BL scores (PB, live API) ┐
local PlayRecords (D-011)├─► PlayEvidence[] ──► ProfileBuilder ──► PlayerProfile
                         │      (MapKey, acc, modifiers, time, source)       │
MapCatalog (model.json) ─┘   effective ratings via speed modifier            ├─► DestinationRules ─► arrived destination
                                                                              ├─► LeaningModel     ─► P(destination)
                                                                              └─► PathState (per chosen path: path pp, D, A)
                                                                                        │
Waypoints (model.json) ─────────────────────────────────────────────────────► WaypointLocator ─► current/next waypoint
                                                                                        │
                                                                              GapAnalyzer ─► push / polish gaps (path priors)
                                                                                        │
MapCatalog + peer popularity (model.json) ──────────────────────────────────► Recommender (D-025) ─► objectives + maps
```
Fresh start (D-010) = evidence filter (ignore/decay before a date) + calibration evidence; same pipeline.

## model.json (proposal) — versioned, aggregates only (D-007)
- `version`, `generatedAt`, `sourceDb` (D-015 file name), `rulesVersion`.
- `thresholds`: destination rules (profiles.THRESHOLDS).
- `leaning`: features, peer baseline (level → medians), scaler, coefficients (exploration 06).
- `paths[]`: per destination: axis, relevance rule id, prior (push share), `waypoints[]` (path-pp range, D, A, acc,
  typical equivalent rank).
- `maps[]`: per BL-ranked difficulty: MapKey, ratings per speed modifier (none/SS/FS/SF: stars, pass, acc, tech,
  predicted acc), map type, duration, pool flags (BL/SS/AccSaber, D-023), **pp lookup curve per speed modifier**,
  peer popularity per (path, waypoint).
- Size estimate: a few MB raw, ~1–2 MB gzipped.

### pp in Core (D-026: lookup curves)
Profiles, path pp and waypoints were all computed in the population DB's pp space (Prod Autoreweighted, D-015), whose
formula (portaBLerebalancer2 `Pp/PpFormula.cs`) has many tuned parameters and per-map reweights. Instead of porting it,
the pipeline exports a **per-map, per-speed-modifier acc→pp lookup curve** (fixed acc grid, monotone, from the DB's
formula/PBs), and Core interpolates. Pros: exact match with the model's space, no formula port, the formula can
change upstream without code changes (new model.json). Cons: bigger file; unranked maps have no curve (only an issue
after D-023, solvable with a ratings-based estimate). This pp is internal only; the platform panel shows live BL pp.
