# Progression design (DRAFT — under discussion, nothing here is final unless marked as a decision)

Requirements: D-003 (hybrid), D-004 (population-driven, archetypes), D-005 (snapshot / fresh start),
D-006 (self-contained). Domain knowledge: see DECISIONS.md.

## Research inputs (2026-11-10)

### Population data available
- `../portaBLerebalancer2/wwwroot/Database.db` (SQLite, ~0.8 GB, from BL admin `/admin/bigexport`):
  146k players, 4.4M ranked PB scores, 3,968 leaderboards, 2022-02 → 2026-10-28. ~29.5k players with ≥20 ranked
  scores. **pp/ratings are rebalanced values**, not live BL. PB only.
- `../portaBLeRebalancer/wwwroot/attempts/` (~900 MB gz): 34M attempts (Clear 8.0M, Fail 6.6M, Restart 5.6M,
  Quit 11.1M, Practice 2.8M) with timestamps, acc, speed, modifiers — the only longitudinal play log.
- Public API alternative: crawl top 20k players' ranked scores ≈ 2.6 h at 5 req/s; all 146k ≈ 10 h.
  `/player/{id}/history?count=N` = daily snapshots back to 2022-09 (acc/pass/tech pp split only since ~2023-12-31);
  values reflect the pp formula of that day → normalise to rank/percentile.
- `/songsuggest` dump is too small/top-heavy (2,614 players × top-20).

- Attempts export details (measured 2026-11-10): `../portaBLeRebalancer/wwwroot/attempts/` — 34.05M attempts,
  178,833 players, 3,960 leaderboards, Timeset 2022-03-10 → **2026-10-07** (2022: 0.15M, 2023: 5.4M, 2024: 9.2M,
  2025: 11.3M, 2026: 8.0M). Columnar little-endian gzip chunks (4M rows), layout + id tables in `attempts.json`
  (written by `portaBLeRebalancer/Refresh/Tools/AttemptsExport.cs`); directly readable with numpy.
  Columns: Id, Player, Leaderboard, Type (0 Unknown,1 Clear,2 Fail,3 Restart,4 Quit,5 Practice), Time, StartTime,
  Speed, Modifiers, AttemptsCount, Timeset, Pauses, Accuracy, MissedNotes, BadCuts, BombCuts, WallsHit, FullCombo,
  HasScore, MaxCombo, Hmd, Controller. Ends ~3 weeks before the population DB (2026-10-28).

### Prior art to reuse
- `portaBLerebalancer2/Services/PlayerStyles.cs`: style from top-25% weighted pp: Acc ≤5★; Midspeed/Tech 5–9★;
  Speed/High Tech >9★; secondary style ≥20%; Modifiers flag ≥50% on SS/FS/SF. Map type from RatingAPI tagger.
- `portaBLe*/SongRec`: comparative-best seeds + peers with nearest pp on the same map (not top scorers);
  `songreceval` = temporal back-test (hide latest 40 scores) → reuse to validate progression predictions.
- SongSuggestCore: co-occurrence links over top-20s, distance |(pp_t/pp_o)^3 − 1|; no time, no skill axes.

### Map features beyond tags (from RatingAPI/Analyzer, MIT)
- Live BL per-leaderboard (individual `/leaderboard/{id}` only, ~4k calls): linearPercentage, peakSustainedEBPM,
  multiRating, sliderPercentage, noteSpan, difficultyStatistics (stacks/towers/sliders/windows/walls/resets/
  bomb avoidances/linear swings), modifiersRating per speed.
- Offline Analyzer only: per-swing frequency, angle strain, rotation, repositioning, hit distance; HardSwingRatio;
  per-second speed/tech/acc `Breakdown` curves (burstiness, stamina profile).
- Tech = AngleStrain^1.25 + 6·Rotation^1.25 (wrist work), damped on low-pass maps. Pass = best windowed average
  swing difficulty (speed). Predicted acc = Acc AI (67 per-note features incl. timing/NJS); acc rating adds speed
  & hit-distance effects.

## Draft concept (Claude recommendation, not decided)
- **Offline pipeline** builds a published, aggregated model; plugin only fetches the player's own data.
- **Player profile at time t**: demonstrated level per skill axis (speed/pass frontier, accuracy vs population at
  equal map difficulty, tech, stamina/sustain, consistency) + style mix vector (PlayerStyles shares, continuous).
- **Archetypes** = destination styles (low-star acc, mid-star midspeed/tech acc, high tech, speed, extreme),
  defined from top-player clusters.
- **Progression path** = waypoints derived from real trajectories of players who reached the chosen archetype
  (history + PB timesets + attempts): at each waypoint, the profile they had, the maps they played next, and
  population acc/pass targets for those maps.
- **Frontier** detection uses fails/quits where available (attempts), else PB distribution.
- **Fresh start**: ignore/decay old scores; place by recent plays + short calibration set; past peak used as a
  "recovery ceiling" (faster expected progress back to it).

## Path selection (D-008)
- One shared player profile; each chosen specialisation is a separate path (track) over that profile.
- Several active paths allowed; recommendations blend them (Claude rec: ask the player for a "focus" path that
  gets most recommendations, others get a share — to be designed).
- Foundation phase shared by all paths until the data shows trajectories diverge.

## Exploration log

### 01 — archetypes (2026-11-10, `training/explore/01_archetypes.py`, outputs `data/explore/01_archetypes/`)
Profile = top-30 plays by pp; 23,525 players with ≥30 ranked Standard scores. Style features are level-independent
(pass below own 95th-pct pass ceiling, tech/pass ratio, acc − predicted acc, map-type pp shares, pp split, star
spread, speed-modifier share, long-map share). Clustered the top 2,000 by global rank.
- Archetypes largely **do emerge** (GMM k=5; finer at k=9): speed (pass ≈11–12, speed-type share 0.66–0.87),
  balanced speed/midspeed (largest), **tech** (tech share 0.65–0.91, tech rating 7.7–9.4), **mid-star acc**
  (~7–9.4★, acc ≈0.965–0.979), **low-star acc** (~4–5.7★, acc ≈0.984–0.991). k=9 also isolates a
  **speed-modifier** group (55% of top-play pp on FS/SF) — matches PlayerStyles' Modifiers flag.
- **"Extreme" does not appear as a separate style** — very high stars show up as the top end of speed/tech clusters.
- Strongest single split = acc specialists vs everyone else (k=2 silhouette 0.60).
- **Bias:** global pp rank under-samples acc specialists (only ~5–9% of top 2,000). Discovery must be niche-aware
  (e.g. best players per style/star band), not global rank.
- **Divergence not answered yet:** at lower ranks, absolute style is confounded by level (players below ~10k mostly
  play 5–9★ maps, which are mostly midspeed). GMM posterior confidence is uninformative (~0.98 everywhere).
  Next: level-conditional style (compare to same-level peers) + longitudinal test (does a lower player's relative
  style predict the archetype they later reach?) using PB timeposts + attempts.

#### 01b — rerun with modifier-adjusted ratings (after user feedback)
- Each play now rated with the ratings of the speed modifier actually used (SF > FS > SS; `ModifiersRating`).
  **Lesson: always use effective (modifier-adjusted) ratings** — nomod ratings made extreme players look like 11★.
- User's example players: extreme → effective 14.1–15.5★, 74–100% of top-play pp ≥13★, 77–100% on speed mods,
  sub-95% share 0.2–1.0; low-star acc → 3.6–3.8★, 99.4–99.5% acc, 100% acc-type maps, ranks 83–178, all in the
  same "pure low-star acc" cluster. Data matches the user's descriptions.
- Extreme still clusters **with speed** on style features (it's mostly a level + modifier difference, not a style one);
  by the user's definition ~1.4% of top 2,000 (rule: ≥50% of top-play pp ≥13★ effective and ≥50% under 95%).
- k=5 with effective ratings: tech (tech share 0.70), speed (pass 11.4, 12% at 13★+), generalist speed/midspeed,
  mid-star acc (9.6★, 96.6%), low-star acc (6.0★, 98.3%; the purest sub-cluster ≈ 3.7–4★, 99%+).

### 02 — trajectories & divergence (2026-11-10, `training/explore/02_trajectories.py`, outputs `data/explore/02_trajectories/`)
- Historical profiles rebuilt quarterly (2023-04 → 2026-10) from **clears** in the attempts export for the 9,987
  players currently ranked ≤ 10k; attempt pp estimated from the population's PB acc→pp curve per leaderboard and
  speed modifier. 4.9M clears → 103k snapshots from 9,677 players. Level at snapshot = "equivalent current rank".
- **Style predicts the final destination early**: balanced accuracy (5-fold, grouped by player) 0.77 at eq. rank
  1–2k, 0.74 at 2–5k, 0.77 at 5–10k, **0.67 at 10–20k (chance 0.25), 0.62 at 20–40k (chance 0.33)**.
  → No hard "foundation" phase visible down to ~40k for players who later climbed; leaning appears early.
- Caveats: survivorship (only players who reached ≤10k); thin low-level history (attempts start 2023, most top
  players were already high); small classes (low-star acc 62, extreme 36 arrived players).
- **Rule problem:** "mid-star acc" (≥50% top pp on 5–9★) is level-confounded. Median acc of players labelled
  mid-star acc: 94.8% (rank ≤5k), 92.1% (5–10k), 87.6% (10–20k), 83.3% (20–50k) → it captures mid-level players,
  not acc players. Needs an accuracy/choice criterion (asked user 2026-11-10).

### 03 — threshold calibration (2026-11-10, `training/explore/03_thresholds.py`, outputs `data/explore/03_thresholds/`)
Rules v3 (`profiles.destination`): family by **main star band** (most top-play pp: <5, 5–9, 9–13, ≥13★ effective);
accuracy **relative to BL predicted acc** (absolute acc is unfair to tech maps); tech share measured **within the band**;
extreme = main band ≥13★ with ≥50% share (sub-95% requirement dropped: user's rank-4 example has only 20% < 95%);
anything not matching → "unspecialised" (no default to speed).
- Best grid: r_low = −0.01, r_mid = −0.01, gap_mid = 0–1 → **all 6 user examples correct**.
- Populations (≥30 scores): low-star acc 60, mid-star midspeed acc 59, **mid-star tech acc 1**, high tech 3,177,
  speed 6,563, extreme 45, unspecialised 13,620 (mostly ranks > 5k: 38% at 5–10k, 78% at 10–20k, 98% at 20–50k).
- Open problems: (1) mid-star tech acc is almost empty — tech maps carry more stars for the same speed, so tech
  acc players' top plays sit in the 9–13★ band; the boundary may need to be speed (pass rating) rather than stars.
  (2) Some high-acc players (e.g. rank 398, 97.6%, 67% tech at 9★+) fall between rules → unspecialised.
  (3) Speed bucket includes rank-2 player at 9.07★ / 98.5% acc (boundary case at 9★).
- Implication: rules define *arrival* at a destination; most players are "unspecialised" → the plugin needs soft
  leaning scores toward each destination (consistent with exploration 02: style predicts destination early).

#### 03b — rules v5: pass + tech ratings only (user suggestion, 2026-11-10)
`profiles.destination` + `profiles.THRESHOLDS`; "typical" = pp-weighted median over top plays (`pass_med`, `tech_med`).
Starting values: acc_resid −0.01, low_pass 3, mid_pass 7, mid_gap 1, tech-led = tech ≥ 0.85·pass AND tech ≥ 5,
tech_arrive 6, speed_arrive 7.5, extreme_pass 12. All 7 user examples correct.
Populations: low-star acc 66, mid-star acc 43, high tech 2,470, speed 4,992, extreme 312, unspecialised 15,642.
Boundary questions put to the user: extreme cutoff (12 catches 24% of top 500, incl. rank 3 at pass 12.3);
mid-star acc vs speed between pass 7 and 7.5 (high-acc players at ranks 18, 41, 42, 70 are unspecialised);
tech-led ratio (rank 1 at pass 9.5 / tech 7.75 is "speed" at ratio 0.85).

#### 03c — rules v6 with D-021 names and rulings (current: `profiles.THRESHOLDS`)
Extreme = typical effective stars ≥ 13; Midspeed up to pass 8 (high end of midspeed); tech-led = tech ≥ 0.8·pass
and ≥ 5; Speed arrival pass ≥ 8; High Tech arrival tech ≥ 6. `check_destinations.py` holds the 11 user anchors:
**11/11 correct.** Populations: Accuracy 66, Midspeed 60, High Tech 2,676, Speed 4,109, Extreme 45 (6.4% of top 500;
rank 3 now Speed), Unspecialised 16,569 (34% at 2–5k, 60% at 5–10k, 89% at 10–20k).

#### 02b — divergence rerun with v6 destinations
Arrived (rank ≤ 10k): Unspecialised 4,133, Speed 3,647, High Tech 2,039, Accuracy 64, Midspeed 60, Extreme 44.
Balanced accuracy predicting final destination from style at earlier level: 0.78 (eq. rank 1–2k), 0.72 (2–5k),
0.76 (5–10k), **0.71 (10–20k, chance 0.25), 0.63 (20–40k, chance 0.33)**. High Tech/Speed leanings identifiable
down to ~40k; Accuracy/Midspeed/Extreme too few climbers at low levels to measure.

### 04 — waypoints per path (2026-11-10, `training/explore/04_waypoints.py`, outputs `data/explore/04_waypoints/`)
Path state per quarterly snapshot (clears of 5,701 arrived players → 60,546 path snapshots): path pp (orders
waypoints), D = typical difficulty on the path axis (top-15 path plays), A = acc vs predicted on those plays.
Path definitions in `profiles.path_plays` / `profiles.path_state`.
- **Speed** (12 wp): D pass 8.5 → 11.2, acc 86.4% → 95.8%, eq. rank ~18k → ~290. Push share 0.40 → 0.53.
- **High Tech** (12 wp): D tech 6.5 → 9.6, acc 83.8% → 94.8%, eq. rank ~18k → ~940. Push share 0.37 → 0.53.
- **Accuracy** (7 wp, ~35 players each): D stays ~3.6–4.7★, acc 98.2% → 99.2%; **push share 0.12–0.31 → almost
  all polish** (matches user intuition: progress = accuracy on the same maps).
- **Midspeed** (6 wp, ~25 players each): D pass 5.1 → 7.4, acc ~97.2–98.3%, push share ~0.3.
- **Extreme** (4 wp, ~25 players each): D 12.1 → 13.4★, **push-dominant (0.63–0.78)**.
- Recommendation candidates ("most common new clears") are popularity-biased: e.g. Speed wp0 (D 8.5) lists a
  pass-12.1 map. → Recommendations must be filtered to a difficulty window around the player's D and the next
  waypoint, and ranked by fit, not by count.
- Small paths (Accuracy/Midspeed/Extreme) rest on 20–40 players per waypoint → noisy; consider adding not-yet-arrived
  players with a strong leaning, and/or fewer waypoints.

### 05 — ranked pool overlap (2026-11-10, `training/explore/05_pool_overlap.py`, outputs `data/explore/05_pools/`)
Public ScoreSaber v2 ranked list + AccSaber ranked difficulties vs BL ranked (population DB), keyed by
(hash, difficulty, mode). BL 3,968 · SS 6,201 · AccSaber 611.
- BL∩SS 2,520 (40.6% of SS); SS-only 3,681 (SS stars median 6.6, p90 11.1); BL-only 1,448.
- **AccSaber∩BL only 47 (7.7%)** — confirmed by AccSaber's own blLeaderboardId; all 47 are pass ≤ 2.9, ~3–5★ BL.
  AccSaber∩SS 95 (15.5%). 506 AccSaber maps are in neither ranked pool.
- BL zones also SS-ranked: Accuracy 787/1253, Midspeed 935/1204, High Tech 361/838, Speed/Extreme 437/673.
- Feasibility of single-pool progression (user question): model is defined in rating space (pass/tech/acc vs
  predicted), so it is pool-agnostic **if every map in the chosen pool has BL-style ratings** → non-BL-ranked maps
  need BL-style ratings → **BL exposes ratings for all maps, including unranked ones** (user; verified) — no
  offline rating needed, only ~1 request per extra hash in the pipeline. AccSaber pool only covers the Accuracy zone.
  Population stats (pass rates, typical acc per waypoint) exist only for BL-ranked maps; elsewhere fall back to the
  AI predicted acc.

### 06 — leaning scores (2026-11-10, `training/explore/06_leaning.py`, outputs `data/explore/06_leaning/`)
Multinomial logistic regression P(destination | style) on 59,729 quarterly snapshots of 5,677 players who later
arrived (Speed 39.9k, High Tech 17.7k, Accuracy 859, Midspeed 794, Extreme 511 snapshots). Exported as JSON
parameters (`leaning_model.json`: features, peer baseline, scaler, coefficients — aggregates only, D-007).
- Raw style features: CV balanced acc 0.80, but today's rank 10–50k Unspecialised come out 57–77% "High Tech" /
  41% "Accuracy" → **level confound** (slow maps have tech > pass, user domain knowledge).
- **Kept: peer-relative features** (each style feature minus the population median at the same level, level =
  log top-30 pp sum, 25 bins). CV balanced acc 0.755 overall; 0.76/0.74/0.73 at eq. rank 1–2k/2–5k/5–10k, 0.59 at
  10–20k, 0.28 at 20–40k (only 1,119 snapshots). When top prob ≥ 0.6: 75–93% correct down to 10k.
  Leanings of today's Unspecialised look plausible (rank 10–50k: Speed 60–66%, High Tech 21–30%, Accuracy 6–10%).
- Tried raw + relative: CV 0.80 (0.51 at 20–40k) but the High Tech bias returns (68–80%) → rejected.
- **Implication:** below ~rank 20k leanings are low-confidence hints → UI shows them as suggestions (D-008 already
  lets the player choose). Improvement: more low-level training history (PB timeposts back to 2022, all climbers
  not only today's top 10k).

### 07 — gaps, objectives, recommendations + back-test (2026-11-10, `training/explore/07_objectives.py`)
Held-out 20% of players (1,079; 6,383 quarter-to-quarter pairs). Popularity learned on train players only.
Back-test measures what players *naturally did next* — a proxy, not proof that a recommendation helps.
- **Push/polish direction** (biggest normalised gap vs actual dominant change): 0.55–0.60 vs majority 0.55–0.79;
  for players who moved up a waypoint 0.598 vs 0.548 → **weak signal**. Path priors are strong (Accuracy ≈ polish,
  Extreme ≈ push, exploration 04).
- **Push recs** (window [D, D_next+step], ranked by next-waypoint peer popularity): hit@10 13–32% vs random-in-window
  9–26% (≈1.5–2×); wider window [D−step, …] 17–32%; **unconstrained peer popularity 26–31% (best)** — players also
  clear many maps below their level and popular maps get played.
- **Polish recs** (cleared maps near D with biggest acc-vs-predicted shortfall): 17–44% vs random-in-window 15–41%
  → shortfall ordering adds little.
- Precision@10 push 2.3–4.2% vs random 1–2.8%; median 7–9.5 new clears per quarter.
- Open product question (asked user): optimise recommendations for natural peer behaviour (popularity) vs strict gap
  targeting vs blend + learning from the plugin's own play log.

### Review round 1 — user's own report (rank 151, Midspeed) 2026-11-10
- Found: placing players by path pp alone put a Midspeed acc player at the **top waypoint of Speed, High Tech and
  Extreme** (breadth/acc on easier path-relevant maps inflates path pp). Fix: on push-driven paths (prior ≥ 0.4:
  Speed, High Tech, Extreme) position = min(pp waypoint, highest waypoint whose smoothed difficulty is reached
  − 0.25·spread); waypoint difficulty made non-decreasing; progress = min(pp progress, difficulty progress).
  Accuracy/Midspeed (polish-dominated) stay placed by path pp — gating Midspeed wrongly put the user at wp 3/6.
- Result: Midspeed top waypoint (correct — 15 Midspeed players above the user); Speed/High Tech/Extreme wp 1 with
  push objectives. Anchors still 11/11.
- Open: the last waypoint is coarse (6 waypoints for Midspeed): near-top players see "top reached" while others are
  still above them → add a "beyond the last waypoint" view (compare to the destination's best players).
- D-027 implemented: past the last waypoint an open-ended next step = max(last waypoint, own level) + the path's
  average waypoint step (difficulty and accuracy). User's Midspeed now: next step pass 6.96 / +0.5% vs predicted with
  polish + push maps. User comment: recommendations "make sense if I was still at my peak" → fresh start (D-010)
  is the answer for returning players.

### Review round 2 — one player (2026-11-10)
Arrived High Tech (typical pass 8.56 / tech 6.91, 11.1★ effective, 93.9%), leaning High Tech 54% / Extreme 38%.
High Tech wp 9/12 (~rank 4.1k) with push toward tech 8.57; Speed wp 8/12; Extreme wp 1/4. Awaiting user verdict.
User verdict round 2: correct (High Tech leaning Extreme).

### Review round 3 — three players (2026-11-10)
- Player A (rank 1,235): model = Unspecialised, leaning Accuracy 81% / Midspeed 19%. **User: a mix of
  accuracy, midspeed, tech and speed; the player considers himself an accuracy + speed player.**
- Player B (rank 452): model = "arrived" Speed, leaning Extreme 72% / High Tech 23%; past the last Speed and
  High Tech waypoints. **User: speed + extreme, also some high tech. "Arrived" read as "reached the top" — but there is
  still a large gap to the best speed player.**
- Player C (rank 3,719): Unspecialised, mixed leaning. **User: correct, mixed.**
- Lessons: (1) a single "arrived" label misleads and hides mixes → propose a style mix (share of top-play pp per path)
  with established styles marked; (2) the last waypoint is too coarse at the top → propose equivalent-rank milestone
  cuts; (3) bug: Extreme path counts all maps (D = 3.59★ for an acc player) → restrict to ≥ 10★.
- Implemented (D-028). Style mixes: user Midspeed 57% ✓ / High Tech 30% / Speed 13%; player A Accuracy 45% ✓ /
  High Tech 26% ✓ / Speed 16% / Midspeed 13%; player B Extreme 44% ✓ / Speed 33% ✓ / High Tech 23%; player C mixed
  (High Tech ✓ only); the round-2 player High Tech 50% ✓ / Speed 36% ✓ / Extreme 14%. Rank 1 = High Tech + Speed.
  Per-map tech rule alone undercounted tech (rank 1 was Speed-first) → BL "tech" tag also counts. Anchors 11/11
  (now "destination among established styles"). Waypoints: Accuracy 8, Midspeed 8, High Tech 13, Speed 15 (down to
  rank 25), Extreme 8.

### In-game review 1 (2026-11-10) — UI changes made
Platforms rows aligned (name left / rank centre / pp-AP right, no country); confirm/start screens centred vertically;
BeatLeader import shows a full-screen progress bar (page progress); Next up rows taller with fixed text heights and
sized buttons; wording without ':' (renders as a dot in Beat Saber): "Next accuracy target  +0.3%" (2026-11-11: the
raw acc-vs-predicted read as "- -0.9%" for a player below prediction → now the average accuracy to reach on the style's
top plays plus the gain, "Next accuracy target - 95.5% (+0.6%)", user choice), "Next clear
target  6.92 pass rating"; "Milestone" replaces waypoint; "Average global rank" replaces "players here"; style name
no longer repeated; milestone progress bar + %; **fresh start / unplayed style recommends starter maps on the easier
side of the first milestone** (Core `PathAnalyzer.Starter`). Log check: BL recorder starts for plugin plays; quit-time
BSML error from our menu button fixed; NalulunaRewinder/CurvedCanvasSettings errors are pre-existing (other mods).
