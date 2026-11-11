# Decisions

Format: ID — date — decision — rationale. Open questions listed below with current recommendation.

## Made

- **D-001 — 2026-11-10 — Target Beat Saber 1.40.8 (for now).**
  User decision. 1.40.8 is also the best-supported version on BeatMods (135 verified mods; BeatLeader,
  ScoreSaber, PlaylistManager have nothing newer there). Keep game-touching code thin so later ports
  (1.42–1.44 = Unity 6000.0, 1.45 = Unity 6000.3) only touch the plugin/UI adapter layer.

- **D-002 — 2026-11-10 — Use Reactive v2** (unreleased `v2-dev` branch of reactive-mono). User decision.
  Avoids an imminent v1→v2 migration. Distribution/coexistence with BeatLeader's v1 on 1.40.8 must be solved (see Q-003).

- **D-003 — 2026-11-10 — Progression model = Option C (hybrid).** User decision.
  The plugin is primarily about tracking *skills*; each platform's actual progress (BL / SS / AccSaber
  rank, pp, AP) is still visible as reference.

- **D-004 — 2026-11-10 — Skill model must be data-driven from the BeatLeader player population.**
  User requirement. Map-type tags and the BL acc/pass/tech split alone are NOT sufficient. Analyze many BL
  player profiles; find lower-ranked players whose profile (difficulty of maps played, accuracy, etc.)
  resembles higher-ranked players, and chart a concrete progression from the lower profile toward the higher.
  Multiple top-end styles are viable on BL and must be modelled as distinct archetypes:
  low-star accuracy, mid-star (midspeed or tech) accuracy, high tech, speed, extreme (very hard maps).

- **D-005 — 2026-11-10 — Two starting modes.** User requirement. (a) Snapshot: start from the player's current
  scores. (b) Fresh start: for players returning after a break who may have lost skill — historical scores
  must not be assumed to reflect current ability.

- **D-006 — 2026-11-10 — Self-contained experience.** User requirement. Opening the plugin from the main menu
  takes over: everything else disappears. Choosing a map in the plugin starts it immediately (no level
  selection). Quitting/finishing returns to the plugin menu.

- **D-007 — 2026-11-10 — Population data: both sources (option 3).** User decision.
  Fit the model (trajectories, frontier detection, archetypes) from the local portaBLe data (Database.db +
  attempts export); routine refreshes (new maps, reweights, live values) from the public BL API.
  **Constraint:** the attempts data may be used for statistics and model training, but raw attempts must
  never be shipped with or retrievable through the plugin or its published model. Publish aggregates only.

- **D-008 — 2026-11-10 — Destination selection = inferred, then confirmed (option C), multi-specialisation allowed.**
  User decision. Plugin suggests the best-matching destination archetypes (plus a "balanced" option) with
  match %, what top players of that style look like, and estimated distance; player confirms and can change later
  without losing history. Players may follow **several specialisations at once**. Shared "foundation" path below
  the level where archetype trajectories diverge (divergence point to be determined from data).

- **D-009 — 2026-11-10 — Waypoint progress = layered (option C).** User decision.
  Waypoints are defined by profile similarity (honest, data-driven). The player sees the 3–4 skill gaps that most
  separate them from the next waypoint, each phrased as a concrete objective with recommended maps. Objectives are
  regenerated as the profile changes. Waypoint reached when the profile matches (single lucky plays can't game it).
  Natural milestones: waypoint reached, gap closed, personal records.

- **D-010 — 2026-11-10 — Fresh start = calibration guided by the old profile (option C).** User decision.
  Old profile sets a sensible starting difficulty (slightly below old level) → short adaptive calibration (~4–6 maps
  covering speed/acc/tech). Past peak kept as a "recovery ceiling"; recovery rate estimated from attempts data
  ("back to your old level in ~N sessions"). Calibration skippable (conservative estimate that self-corrects).
  Plugin suggests fresh start when last ranked play > ~2–3 months ago, never forces it.

- **D-011 — 2026-11-10 — The plugin keeps its own local play log.** User decision.
  Every level started through the plugin records its result (clear/fail/quit/restart, acc, misses, modifiers, time…).
  This log is the primary evidence for the player's current profile in both starting modes; BL data seeds the
  initial profile. Rationale: BL stores PBs only, so post-break sub-PB plays are invisible via the public API.

- **D-012 — 2026-11-10 — Build order = two parallel tracks, model-led (option C).** User decision.
  Track 1 (model): offline pipeline on portaBLe data + CLI that prints any BL player's profile, archetype matches,
  next waypoint, gaps, objectives; validated by the user's expert review and a temporal back-test.
  Track 2 (plugin shell, de-risking): menu button + self-contained flow, download + direct start of a hard-coded map,
  return to our menu, BL upload still working, local play log, one Reactive v2 screen.
  Both share a Unity-free, fully tested Core library (profile/path/objective models and logic).

- **D-013 — 2026-11-10 — Model tooling = Python for training, C# for everything else (option B, "for now").**
  User decision. Feature extraction and player-side inference live ONLY in the C# Core (identical in CLI and
  plugin by construction). C# pipeline uses Core to extract features → tidy data files → Python (scikit-learn/pandas)
  explores and fits → versioned model JSON → consumed by Core. Revisit if Python step becomes a burden.

- **D-014 — 2026-11-10 — Repository layout & tooling.** User approved.
  git repo (branch `main`, local only); legacy BSMT template deleted (user approval). Layout: `src/` (Core
  netstandard2.0 no I/O; Data netstandard2.0 API/persistence; Plugin net48 — not yet created, waits for the
  Reactive v2 dev instance), `tools/` (Tools net10 console: extract / player / backtest), `training/` (Python),
  `tests/` (net10 xUnit), `data/` (gitignored, never committed). SDK pinned to .NET 10 via `global.json`
  (machine default is an 11 RC; Reactive.Compiler needs Roslyn 5 = .NET 10). Solution file = `SkillProgression.slnx`.

- **D-015 — 2026-11-10 — Population database = "Prod Autoreweighted".** User decision.
  File: `../portaBLerebalancer2/wwwroot/db-20261104211323-3944.db` (newest catalog entry "Current production
  (autoreweighted)"; 146,111 players, 4,415,434 scores, 3,968 leaderboards, scores up to 2026-10-28).
  Open the DB read-only (`mode=ro`). Other variants only for comparison.

- **D-016 — 2026-11-10 — Dev environment = the existing 1.40.8 instance (no separate copy).** User decision.
  Installed BL `reactive2`+1.40.8 patch build and Reactive v2 (`v2-dev` 5e6a0a0); removed stale
  Reactive.BeatSaber.Components.dll. Backup + restore steps: `data/dev-env/` (gitignored, local only).
  Consequence: this instance's BeatLeader lacks 0.10.666's extra features until restored.

- **D-017 — 2026-11-10 — Two-stage feature work (refines D-013).** User approved.
  Exploration: Python reads the population DB and attempts directly to prototype features (outputs stay in `data/`).
  Production: chosen features are implemented + unit-tested in C# Core; `extract` regenerates feature tables via Core;
  the published model is fitted only on Core-computed features. Python-only features are discarded.

- **D-018 — 2026-11-10 — Destinations are expert-defined rules; data tunes thresholds (option B).** User decision.
  Five destinations: **low-star acc** (most top-play pp on < 5★, very high acc; path never pushes toward midspeed),
  **mid-star acc** (5–9★, high acc; midspeed/tech variants to be checked with data), **high tech**, **speed**,
  **extreme** (most top-play pp at ≥ 13★ effective, mostly < 95%). All ratings modifier-adjusted.
  Speed modifiers are NOT a destination: a per-path option (training tool / higher effective stars), not suggested
  on low-star maps. Data supplies thresholds, population sizes, trajectories, divergence points, objectives.

- **D-019 — 2026-11-10 — Mid-star acc = accuracy + choice, split into midspeed and tech variants.** User decision.
  A mid-star acc player has high accuracy on ~5–9★ maps AND could clear harder maps but earns pp from accuracy there.
  The range splits into **mid-star midspeed acc** and **mid-star tech acc** (tech maps on the slower/easier side
  compared to high tech). Many players strongly prefer tech over non-tech midspeed → the plugin must offer both.
  Destinations are now six: low-star acc, mid-star midspeed acc, mid-star tech acc, high tech, speed, extreme
  (+ a "not yet specialised" state for players matching none).

- **D-020 — 2026-11-10 — Mid-star tech acc is a phase, not a destination; "mid-star" is defined by speed.** User decision.
  Supersedes the six-destination list of D-019. Destinations are back to five: **low-star acc, mid-star (midspeed)
  acc, high tech, speed, extreme**. Mid-star tech acc = an early phase of the high tech path (slower tech maps), which
  still satisfies D-019's "offer the tech option". "Mid-star" is delimited by **pass rating (speed)** of top plays,
  not stars (tech maps carry more stars for the same speed). High tech example player provided (anchor list, `data/anchors.json`).

- **D-021 — 2026-11-10 — Destination names and boundary rulings.** User decision.
  Names: **Accuracy** (was low-star acc), **Midspeed** (was mid-star acc), **High Tech** (short: Tech), **Speed**,
  **Extreme**, **Unspecialised**. Rulings: (1) Extreme = mostly very high-star maps (high pass AND tech at once),
  challenge-seeking, usually low accuracy there; naturally overlaps Speed/High Tech — if too hard to separate, fold
  into Speed (or High Tech). (2) High-acc players at pass ≈7–7.5 (ranks 18, 41, 42) are Midspeed (high end of
  midspeed). (3) The rank-1 player (pass 9.5 / tech 7.75) is High Tech (best high-tech player in the world,
  also decent at speed).

- **D-022 — 2026-11-10 — Waypoint level = hybrid (option C); improvement is two-dimensional.** User decision.
  Waypoints of each path are ordered by that path's own skill axis (Speed: pass rating; High Tech: tech rating;
  Accuracy: acc vs predicted on slow maps; Midspeed: acc on pass ~5–8 maps; Extreme: effective stars) and labelled
  with the typical equivalent global rank. User: on every path there are two kinds of improvement — **playing harder
  maps (higher ratings)** or **improving accuracy on the same ratings**. → Each waypoint has a difficulty part and an
  accuracy-at-difficulty part; gaps/objectives come as "push" (harder maps) and "polish" (better acc at current
  ratings). Claude proposal: order waypoints by path-specific weighted pp (pp already combines both), keep the two
  components separate inside each waypoint.

- **D-023 — 2026-11-10 — Map-pool focus after the MVP (option B).** User decision.
  MVP recommends BL-ranked maps only. From day one the data model and pipeline store, per map, pool membership
  (BL / ScoreSaber / AccSaber ranked) and BL ratings (available for all maps), so pool choice later = filter + SS/AccSaber
  panels. AccSaber focus only for the Accuracy destination.

- **D-024 — 2026-11-10 — Self-contained flow shows the game's gameplay setup panel.** User request (refines D-006).
  Our flow's left screen = the game's own GameplaySetupViewController (modifiers, player options, environments,
  colors), set up like solo mode; levels start with its values. Everything else in the menu stays hidden.
  Leaving the flow = the game's standard back arrow next to the title (user preference), no custom Exit button.

- **D-025 — 2026-11-10 — Recommendations = blend, then learn (option C).** User decision.
  Candidates from a gap-targeted difficulty window (slightly below the player's level → just past the next waypoint),
  ranked by what next-waypoint peers played, with a diversity rule (no single map type dominating). Push and polish
  both shown, ordered by normalised gap weighted by the path's prior (Accuracy → polish first, Extreme → push first).
  Later: use the plugin's play log to measure which recommendations were followed and whether those players reached
  the next waypoint faster, and re-weight the ranking.

- **D-026 — 2026-11-10 — Architecture approved; pp via lookup curves (option B).** User decision.
  docs/ARCHITECTURE.md (Core data flow, model.json contract) approved. Core does not port the population DB's pp
  formula: model.json ships per-map, per-speed-modifier acc→pp lookup curves; Core interpolates. Internal pp only.

- **D-027 — 2026-11-10 — There is always a next recommendation.** User requirement: even the best player in the
  game must get recommendations for what to play next to improve. Beyond a path's last waypoint the plugin generates
  an open-ended "next step" (beyond the player's own level by the path's typical step) instead of "top reached".

- **D-028 — 2026-11-10 — Style mix replaces the single "arrived" label; rank-milestone waypoints.** User approved.
  Player header = share of top-30 pp per style (each play in exactly one style: Extreme ≥13★ eff., Accuracy pass ≤3,
  High Tech = tech-led or BL-tagged "tech", Midspeed pass ≤8, Speed otherwise) with **established** styles marked
  (share ≥25%, Extreme ≥40%, plus that destination's criteria on those plays). "Arrived" no longer shown: it read as
  "reached the top". Waypoints cut at equivalent-rank milestones (40k … 25/10) — finer near the top. Extreme path only
  counts maps ≥10★ effective.

- **D-029 — 2026-11-10 — In-game experience rules (UX review of the MVP structure).** User decisions.
  1. First run asks: **start fresh** or **import from BeatLeader**. The choice stays available later (settings) and
     changing it always requires confirmation.
  2. The player can **switch the style they focus on at any time**; recommendations switch automatically. "Next up"
     follows the **current focus style** (not a blend). Any recommendation can be **skipped** to get a new one.
  3. **Smaller steps:** add more, smaller steps between waypoints so objectives feel achievable in the short term.
  4. Right panel shows live **BeatLeader, ScoreSaber and AccSaber** rank/pp/AP (supersedes "SS/AccSaber later").
  5. Path detail / overall layout: user wants to judge it in-game before final decisions.

- **D-030 — 2026-11-10 — Missing a target never costs anything; 3 misses move a map down.** User approved.
  Profile uses best plays, so bad runs never lower the level; every play is still logged. Result line shows the delta
  to target / PB, map stays in Next up with Retry. After **3 unsuccessful attempts** on a map (fail, or clear below
  target — Claude's interpretation of "3 fails"), the map moves down the Next up list and an easier map for the same
  objective comes up. Skipping is always allowed and is not a miss.

- **D-031 — 2026-11-10 — "Explore" side feature.** User request. A button next to Start mode opens Explore (back arrow
  returns to Training): fresh lists of maps for a chosen style, mode **Improve a score** (played, expected to beat the
  PB) or **Never played**, a slider **Easier ↔ Harder** (steps of ~0.35 × the path's difficulty spread around the
  player's level), "New list" for a different random pick, and a personal **expected accuracy** per map
  (Claude design: map's BL predicted acc + the player's own residual vs prediction on that style as a pp-weighted
  linear function of difficulty, slope ≤ 0). Independent of objectives, but plays use the same launcher/log so they
  can complete a milestone/step. Difficulty labels everywhere: [E] [N] [H] [Ex] [Ex+] (user format).

- **D-032 — 2026-11-10 — Explore when maps run out: auto-widen + counts (A + B).** User decision.
  A: below 10 candidates the window widens one step at a time (max 4) and the screen says "Few maps here - range
  widened to …". B: a line under the slider shows candidates per position for the current mode/style. Fix: the window
  is clamped to where candidates of the current mode exist (Extreme/low High Tech pointed at empty ranges).
  Data point: the user has played 2,262 of 3,894 BL-ranked difficulties (e.g. 42 unplayed Midspeed maps at their
  level). Later (with D-023): unranked maps — BeatSaver catalogue (SongDetailsCache / API), BL ratings fetched per
  candidate, quality filter (upvote ratio, votes, curated/verified) — user unsure how it would work, open design.

- **D-033 — 2026-11-10 — Achievements & history (approved list).** Types: milestone reached (from milestone 2),
  short-term step completed, style established, personal record (hardest clear per path in 0.5 bands from 1.0),
  consistency (5 different maps at your level on a path, cleared through the plugin in a week), comeback (fresh start,
  back to a pre-fresh-start milestone), platform rank thresholds (10k … 10), and official points thresholds
  (user addition, tuned on live leaderboards 2026-11-10 to start at 8,000 pp / 4,000 AP and tighten near the
  top: pp 8k,10k,12k,13.5k,15k,16k … 22.5k in 500 steps; AccSaber Overall 4k,8k,12k,16k,20k,22.5k,24k … 33k;
  AccSaber categories 4k,5.5k,7k,8k,8.5k … 11.5k). Stable ids, never twice; first run and
  first-seen platforms recorded quietly as "earned before". Daily snapshots + BL daily rank history back-fill in
  `UserData/SkillProgression/progress.json`. UI: Achievements button → "Achievements | History" (Almost there with
  progress bars, Unlocked list; global-rank line chart (log, inverted) and focus-style milestone chart); unlocks
  appended to the result line after a play.

- **D-034 — 2026-11-10 — Achievements by type, history by source (user review of D-033).** Achievements tab: a
  dropdown picks the type (Almost there, Milestones, Personal records, Short-term steps, Styles, Consistency,
  Comebacks, Platform ranks, Platform pp / AP); the list shows every achievement of that type in order, unlocked
  (✓ + date or "earned before") and locked (progress bar on the next one, "locked" after). Unfinished progress is
  never shown as 100%: record progress is measured from the previous 0.5 band and capped at 99%, percentages are
  rounded down. History tab: a dropdown picks BeatLeader / ScoreSaber / AccSaber rank (each back-filled once from
  its API: BL daily since 2022, SS ~7 months, AccSaber a few points — extended by daily snapshots) or a style's
  milestone chart (defaults to BeatLeader). Wording "milestone 8, 40% toward milestone 9" replaces "Milestone 8.4".

- **D-035 — 2026-11-10 — Achievement set trimmed and made style-aware (user review of D-034).** Style buttons (as on
  Training, opening on the focus style) filter Milestones and Consistency; Almost there = the selected style + the
  style-free types. Types now: Almost there, Milestones, Consistency, Personal records, Platform ranks, Platform pp / AP.
  Removed: styles established (players specialise, so the set can't be completed), short-term steps (unclear),
  comebacks (don't push players into a fresh start). Consistency = tiers 5/10/15/20/25 different maps at your level on
  the style, cleared through the plugin within 7 days. Personal records are style-free, in 1-point tiers: hardest
  star (1-15), pass (1-18), tech (1-17) and acc (1-16) rating cleared (caps = top of the ranked pool). "Hide achieved"
  button. Retired achievements stay in progress.json but aren't shown; records seen for the first time on an older
  progress file are recorded quietly. "Highest accuracy on X stars" = a records table (best accuracy on N to N+1 star
  maps, effective ratings), not achievements (user choice), shown in History ("Best accuracy per star" source).
  Lists show only the next locked achievement of each series (user): unlocked ones + e.g. "top 100" at rank 250;
  "top 50" appears once top 100 is unlocked. Clear records have no progress bar or %: cleared or not (user); they never appear in
  Almost there.

- **D-036 — 2026-11-10 — Play log v2: No Fail soft fails and pauses (user go-ahead).** PlayRecord version 2 adds
  `SoftFailed` (No Fail on and energy reached 0 during a plugin-started play: the game reports a clear),
  `SoftFailSongTime` and `Pauses`, tracked in the standard game scene (SiraUtil Location.StandardPlayer,
  `GameplayTracker`; only while a plugin-started play is active, restarts tracked afresh). A soft fail is not a pass:
  it is not evidence, never meets a target and counts as a miss (3-miss rule); the result line is the normal
  "Failed" one (user). Detection: energy <= 0 each frame while the play was started with No Fail (the energy counter's
  event and noFail flag don't reflect the modifier on 1.40.8; verified in FPFC). Version 1
  records load unchanged (no soft fail, 0 pauses). Pauses are recorded only, for later use (adaptive steps, D-030).

- **D-037 — 2026-11-10 — Map rows (user).** Training Next up and Explore titles show the star rating after the
  difficulty ("Song [Ex+]  9.47★"; same ratings as the axis value, effective for speed-modifier PBs). The grey detail
  line uses fixed columns (TMP `<pos>`) so kind, axis rating, target / expected, now (0% when unplayed) / best and misses line up.

- **D-038 — 2026-11-10 — Polish around the next waypoint's difficulty (user choice).** A waypoint's accuracy target is
  what its players reach on maps of its typical difficulty; beating predicted acc is harder on harder maps. When the
  player's path difficulty is above the next waypoint's, accuracy (polish) objectives pick their played maps within
  ±0.75 of that difficulty (e.g. Accuracy 7→8: ~4.25★ instead of the player's 6.5★), keeping the same target. Falls
  back to the player's own level when nothing there needs polishing. Alternative not taken: difficulty-adjusted targets.

- **D-039 — 2026-11-10 — Progress bar = profile distance (implements D-009; user choice).** Progress toward the next
  milestone blends three dimensions — path pp, difficulty, accuracy vs predicted — each a 0..1 share of the step from
  the current to the next milestone's typical players, capped at 1 so a surplus can't hide a gap, weighted by the
  squared step size in player-spread units (pp's step = 1). Gated paths: the bar can't exceed difficulty progress.
  Milestones themselves are still reached by path pp (D-028). Per-dimension values kept in `DimensionProgress`.

- **D-040 — 2026-11-10 — Fresh-start calibration = adaptive, one map at a time (implements D-010; user choice).**
  After a fresh start, a style with an old level (from the full BeatLeader history) and no progress since the fresh
  start is calibrated when focused: Next up shows one map, first at 85% of the old level on the style's axis; a success
  (pass, not a No Fail soft fail, at the target = predicted acc + nearest waypoint's residual) steps up, a miss steps
  down, first step 10% of the old level, halved at each change of direction; ends after 5 maps or once the step is
  under 2.5%. The estimate is capped at the old level (recovery ceiling). Afterwards, starter maps aim at the estimate
  until the style has enough plays for normal analysis. "Skip calibration" → cautious level (85% of the old level).
  Skipping a calibration map picks another at the same level. Picks are stored per style in state.json and cleared
  when the start mode changes. Not done: recovery-rate estimate (needs attempts data, D-007).

- **D-041 — 2026-11-10 — ScoreSaber / AccSaber pools = a recommendation filter (implements D-023; user choice).** A "Map
  pool" dropdown next to the style buttons (BeatLeader / ScoreSaber / AccSaber / Any ranked, saved in state.json)
  decides which maps Next up, calibration and Explore recommend (accuracy polish and Explore "Improve" too). Progress
  and milestones are measured the same way. AccSaber applies to the Accuracy style only; elsewhere it falls back to
  BeatLeader. The model now also holds the SS- / AccSaber-ranked maps that aren't BeatLeader-ranked, with BeatLeader's
  own ratings (`training/fetch_pool_ratings.py`, `/leaderboards/hash/{hash}`, cached in data/explore/08_pool_ratings)
  and acc→pp curves = mean of the 5 BeatLeader curves with the closest (pass, acc, tech, predicted acc) ratings (D-026
  lookup curves, no formula port). They have no peer popularity. Plays on them now also earn (estimated) pp.
  Default and first choice = "Any ranked" (user; state.json v2 migrates older files to it). The same dropdown sits next
  to the style buttons in Explore.

- **D-042 — 2026-11-10 — Adaptive step sizes (refines D-029/D-030; user choices).** Signals: success, fail, No Fail
  soft fail (pauses not used). Over the last 20 plugin plays on the focus style's maps (practice, quits, restarts
  ignored): reaching the target +1, fail / soft fail -1, clear under the target 0. The mean sets the short-term steps
  per waypoint gap: 4 - 1 x mean when >= 0 (down to 3), 4 - 2 x mean when < 0 (up to 6); 4 with fewer than 5 plays.
  The per-map accuracy gain cap (0.5% at 4 steps) scales by 4 / steps. PlayRecord now stores the target shown
  (`TargetAccuracy`: Next up / calibration target, Explore expected acc); older records without one count a pass as a
  success. Missing still never lowers the level (D-030).

- **D-043 — 2026-11-10 — Release and unranked maps (user).** Release = a .zip on a GitHub release with everything
  needed (plugin, `UserData/SkillProgression/model.json.gz`; Core/Data libs no longer separate since D-046; mod
  dependencies incl. the compression libs are declared in manifest.json, not bundled), shipped by the user only once Reactive v2 is
  public together with a matching BeatLeader build (closes Q-003 and model hosting: the model is bundled, no download).
  Unranked maps in Explore (D-032) postponed: BeatLeader ratings would have to be fetched per song for the whole
  BeatSaver catalogue (one call per song at ~3-4 / s), many hours to more than a day.
- **D-044 — 2026-11-11 — Mod requirement filter in Explore (user choices). SUPERSEDED same day by D-045: filter,
  `MapEntry.Requirements` and pipeline bits removed (BeatLeader doesn't rate modded maps, unrated maps are excluded).** A "Mods" dropdown in Explore: None
  (default) / Noodle / Mapping Ext. / Vivify / All, meaning **only that mod** (None = vanilla maps only, a mod = only maps
  using it, All = everything); applies to both Explore modes, not to Training; kept per session (not saved). Stored in
  model.json as `MapEntry.Requirements`, a list of **SongCore names** ("Noodle Extensions", "Mapping Extensions",
  "Vivify"), omitted when empty, so BeatLeader and future BeatSaver/curated sources map into one format. **Chroma is
  ignored** (counts as vanilla): sources can't tell required from suggested Chroma, and the launch check catches the rare
  map that truly requires it. Chosen over an automatic installed-mods filter because curated maps are planned for
  Explore. Source today: BL `difficulty.requirements` bits for the SS/AccSaber extras; BL-ranked maps are assumed vanilla
  (not verified; the population DB has no requirements column). Current pools contain **no** such rated map.
  Safety net: `LevelLauncher` refuses to start a map whose `_requirements` aren't registered SongCore capabilities.
- **D-045 — 2026-11-11 — Curated maps in Explore (user choices).** (a) **Explore only**: Training stays ranked, and
  plays on curated maps never count for progression — `ProgressionEngine.Catalog` holds ranked maps only, Explore uses
  `ExploreCatalog` (all maps); Explore's level and accuracy estimate also come from ranked plays only. (b) **Build time,
  in the model**: `training/fetch_curated.py` (BeatSaver curated list + BL ratings per song, cached) → `build_model.py`
  sets `Curated`/`BeatSaverScore` on model maps and adds rated curated maps ranked nowhere (ratings, no pp curves). Maps
  curated after a build appear with the next model. (c) **Unrated maps excluded**: BL doesn't rate modded maps, so the
  D-044 Mods filter was removed entirely (launch check kept). (d) **UI**: "Curated" is an extra entry in Explore's pool
  dropdown; ranked entries still sync with Training, Curated is Explore-only and kept per session. (e) **Improve a
  score** on Curated also uses the player's **unranked BeatLeader PBs** (`type=unranked`, own cache file
  `beatleader-unranked-scores.json`, synced once per session on first Curated use; never used by progression).
  Claude choice: Curated "never played" lists are ordered by BeatSaver score (no peer popularity without population
  scores).
  First build (2026-11-11): 13,020 curated songs → 29,210 rated Standard difficulties (1,336 unrated skipped); 3,816
  model maps flagged curated, 25,394 curated-only maps added; model 33,455 maps, 41.5 MB raw / 7.3 MB gz (was 4.2 MB);
  check-anchors 11/11.
- **D-046 — 2026-11-11 — One shipped DLL: Core/Data sources compiled into the plugin (user choice, option B).** The
  plugin project compiles `src/SkillProgression.Core/**/*.cs` and `src/SkillProgression.Data/**/*.cs` (linked, minus
  obj/bin and Core's duplicate `IsExternalInit`) instead of referencing the projects; nothing goes to `Libs/` any more
  (the game's Newtonsoft.Json stays external). Core and Data remain separate projects for the tests and the CLI, and the
  plugin must not reference them. Chosen over ILRepack (no extra build dependency, no post-build merge, PDB line numbers
  kept). All projects share LangVersion/Nullable via Directory.Build.props, so the code compiles identically.
- **D-047 — 2026-11-11 — Explore's "Your level" = the difficulty Training works at (user choice, option A).** Explore
  centred on the player's own level (pp-weighted median of top style plays), Training on its top objective: on Accuracy
  the user's own level was 6.45★ (low-pass maps up to 7.5★ count as Accuracy) while Training polished around the next
  waypoint's 4.25★ (D-038), so Explore showed 6–6.7★ maps. Now slider 0 = `Objective.MapCentre` of the style's top
  objective (push: next waypoint difficulty; polish: D-038 centre), else the starter's, else the player's own level;
  computed with Training's pool and evidence (ranked only). Applies to every style: push styles centre on the next
  milestone (user's data: Speed 6.66 → 8.16 pass, Extreme 10.41 → 12.95★). Follow-ups (user, same day): slider 0 is
  labelled **"Training level"**; after a **fresh start** Explore uses the calibrated level like Training (calibration
  estimate when done, next calibration map's level while running, cautious level when skipped; read-only, no picks);
  **easier positions are shares of the level** (-1/-2/-3 = 85% / 70% / 50%; harder stays level + 1..3 steps of
  0.35 × spread), because steps alone only reached ~0.8★ below the level on Accuracy (user's data now: 4.25 → 3.61 /
  2.97 / 2.12★). The window still clamps to where the style has maps (High Tech / Extreme bottom out at -2).
- **D-048 — 2026-11-11 — AccSaber pool = Accuracy only (user; refines D-041).** Choosing AccSaber (Training or Explore)
  focuses the Accuracy style and locks the style buttons (Reactive's `BsSegmentedControl` can only disable the whole
  control, so all five look disabled with Accuracy selected). With AccSaber, every AccSaber map is an Accuracy map for
  **recommendations** (Next up, starters, calibration, Explore) via `PathRules.IsRecommendable` — 4 of ~600 have a pass
  rating above the Accuracy limit. Progress is still measured with the style rules (`IsRelevant`), pool-independent.

## Domain knowledge from the user (authoritative)
- **AccSaber** is purely accuracy, on easier maps — roughly 0–5★ BL equivalent. "True/Standard/Tech acc" are
  complexity bands of acc maps; "tech acc" ≈ the hardest acc maps, NOT BL-style tech. Beyond that, maps become
  slow-midspeed.
- **BL pass rating** correlates directly with map speed; a good indicator the player can play faster maps.
- **BL acc rating** derives from the AI predicted accuracy (≈ what a top player reaches) and includes a speed
  factor. It says little on its own. Style specialists can beat predicted acc by up to ~1 point.
- **BL tech rating** = wrist work: rotation and angle strain. Details: `../portaBLerebalancer2`
  (contains RatingAPI and Analyzer projects).

- **Extreme players** (user, 2026-11-10): play 13★ and above, often with speed modifiers that raise effective stars
  further; usually most top plays are under 95% acc. Three example players provided (anchor list, `data/anchors.json`).
- **Low-star acc players** (user): some players are only good at < 5★ maps; worth less pp, but some players don't
  want to become midspeed specialists — the plugin must respect that (don't force a move to midspeed).
  Three example players provided (anchor list, `data/anchors.json`).
- **Players below ~rank 20k are mostly mixed** (user, 2026-11-10) unless they were already focusing on something →
  low-level leanings are soft suggestions by nature; more low-level training data is low priority.
- **AccSaber pool = Accuracy destination only** (user, 2026-11-10; data: all BL∩AccSaber maps are pass ≤ 2.9).
- **Speed modifiers** (user): usable with any style; generally not recommended on low-star maps.

## Open (to discuss with the user)

| ID | Question | Current recommendation (not decided) |
|----|----------|--------------------------------------|
| Q-004 | MVP data scope | BeatLeader first; AccSaber/SS as reference panels |
| Q-005 | Auth: anonymous public reads vs signing in | Anonymous for MVP |
| Q-007 | Persistence: JSON files in UserData | JSON; no native SQLite available |
| Q-011 | Published model: aggregate-only — should it avoid per-player records entirely (e.g. no anonymised kNN reference population)? | Aggregate-only; revisit if kNN needs it |

Superseded: Q-003 → D-043; Q-012 → D-023; Q-006, Q-009 → D-014; Q-010 → D-007; Q-001 → D-003; Q-002 → D-002; Q-008 → D-006 (direct start, not playlists/level select).
