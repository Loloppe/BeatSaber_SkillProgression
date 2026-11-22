# External API integrations — verified facts (as of 2026-11-10)

All read endpoints below work anonymously unless stated. Deserialize tolerantly — fields get added.

## BeatLeader (primary: player scores + ranked pool)
Base: `https://api.beatleader.com`. Local server clone (`../beatleader-server`) is ~1 year stale; live API has extra fields.

**Player id**: Steam64 or OculusPC id; in-game get it like the BL mod does: `IPlatform.user.userId`.
`GET /player/{id}?stats=true` → pp, accPp, passPp, techPp, rank, scoreStats{averageRankedAccuracy, topPp, …}.

**Scores (PB only, one per leaderboard)**: `GET /player/{id}/scores`
- `page` (1-based), `count` ≤ 100, `sortBy` (date|pp|accPP|passPP|techPP|acc|stars|…), `order`,
  `type` (3=ranked), `time_from`/`time_to` (unix s) → **incremental sync**, `includeIO=true` → `scoreImprovement`.
  `type` is a `DifficultyStatus` by name: `type=ranked` / `type=unranked` (status 0; verified live 2026-11-11, used by
  Explore's Curated pool, D-045; progression on SS / AccSaber-ranked maps, D-054). Unranked scores carry no stars.
  **AccSaber scores are BeatLeader scores** (`blScoreId` on each AccSaber score); only 47 of 602 AccSaber maps are BL-ranked.
- Score: accuracy, pp, passPP, accPP, techPP, fcAccuracy, fcPp, modifiers, missedNotes, badCuts, bombCuts,
  wallsHit, pauses, fullCombo, maxCombo, maxStreak, accLeft/accRight, timeset/timepost, replay (.bsor URL), rank.
- `playCount`/`lastTryTime` zeroed unless authenticated as the player or player made stats public.
- `/player/{id}/scores/compact` — lighter, default sort date.

**All attempts (fails/restarts)**: `GET /player/{id}/scoresstats` — **401 unless authed as player** or ShowStatsPublic.

**History**: `/player/{id}/history?count=N` daily snapshots (pp, accPp, passPp, techPp, rank, avg acc, …).
PB endpoint only keeps current PB (+ previous via scoreImprovement) → **past states cannot be fully
reconstructed; the plugin must snapshot its own computed progression over time.**

**Per-score detail**: `/score/statistic/{scoreId}` → hitTracker, accuracyTracker (pre/post swing, timing,
gridAcc[12], avg cut), winTracker, scoreGraphTracker (acc over time).

**Ranked pool**: `GET /leaderboards?type=ranking&count=500&page=N` (~4866 entries, ~10 pages, mixed status
3 ranked / 7 OST / 4 unrankable). Difficulty: stars, passRating, accRating, techRating, predictedAcc,
type (map-type bit flags: 1 Acc, 2 Tech, 4 Midspeed, 8 Speed, 16 Fitbeat, 32 Linear, 64 BombAvoidance —
see `/mod/maptypes`), modifiersRating, rankedTime. No njs/nps/duration in the lean listing (full `/leaderboards` has them).
Rating change history: `/leaderboard/{id}` → `changes[]`.
**All scores of a leaderboard**: `GET /leaderboard/{id}/scoregraph` (verified live anonymously 2026-11-14, also on
unranked maps) → array of {playerId, accuracy (×100), pp, rank, playerRank, modifiers, timepost, pauses, mistakes, …,
playerName/avatar}. One call per map, no paging/time filter; AccSaber pool: 611 calls ≈ 6 min at ~3/s, 110 MB raw,
median 426 scores per map, max 6,733 (D-058).
**Ratings exist for unranked maps too** (user, verified 2026-11-10 anonymously via `/leaderboards/hash/{hash}`:
an SS-only and an AccSaber-only map, status 0, returned stars/pass/acc/tech/predictedAcc). Map `type` may be 0
(untagged) on unranked maps. No pp/rank for unranked maps.
`/leaderboards/hash/{hash}` difficulties also carry `njs`, `noteJumpStartBeatOffset`, `nps`, `notes`, `duration` (used for NJS, D-056, and NPS, D-059). No swing count / SPS field (checked 2026-11-15).
**Requirements** (`difficulty.requirements`, bit flags, `beatleader-server` `Requirements` enum): 2 Chroma, 4 Noodles,
8 MappingExtensions, 16 Cinema, 32 V3, 64 OptionalProperties, 128 VNJS, 256 Vivify, 512 V3Pepega, 1024 GroupLighting.
Chroma/Noodles/ME/Cinema are copied from BeatSaver's per-diff `chroma`/`ne`/`me`/`cinema` flags (map *uses* the mod,
required or suggested); Vivify comes from the diff's Info.dat `_requirements` (D-044).
**BeatLeader does not rate modded maps** (checked 2026-11-11: 9/9 curated Noodle maps, incl. Vivify ones, and the
SS-ranked Noodle map "mumei" have no stars/pass/acc/tech; predictedAcc may still be present).

**pp curve (D-060, verified live 2026-11-22)**: `GET /pp/curve?leaderboardId={id}&modifiers={FS,GN,…}` →
{leaderboardId, status, hasPp, modifiers, stars, points: [[acc, pp], …]} (~290 points, pp rounded to 2 decimals, ascending
acc). Speed modifiers change stars and pp; GN / PM leave them unchanged. Unranked → `hasPp: false`, empty points. The
leaderboard id comes from `/leaderboards/hash/{hash}` (`leaderboards[].id`; match difficultyName **and** modeName).
PPCounter (Loloppe) uses exactly this since its PPv4 change (2026-10-27); its older local pass/acc/tech formula is obsolete.
`/leaderboards/hash` also has `song.bpm` and `song.coverImage` (used for base JD and row covers).

**Rate limits**: GET 50 / 10 s per IP; `/players` 10/10 s; `/maps` 8/s. 429 + Retry-After. No ETag.
No published third-party ToS found → send descriptive User-Agent, throttle ~3 req/s.

**Auth**: cookie session via Steam/Oculus ticket (`POST /signin`), OAuth2 only grants identity/clan scopes.
BL mod session is `internal` — don't reuse via reflection. BL mod publicly (but `[Obsolete]`) exposes
`UploadReplayRequest.StateChangedEvent` (score-uploaded event); local replays in `UserData/BeatLeader/Replays`.

**Map key**: `song.hash` + `difficultyName` + `modeName`. Leaderboard id = songId+diff+mode, but has
re-version suffixes (`xx`) — **never parse ids**.

## ScoreSaber (ranked pool + stars)
Docs/spec: `https://scoresaber.com/api/openapi.json` (API v2). v1 still live (14 items/page).
- `GET /api/v2/leaderboards?status=RANKED&limit=100&page=N` (~6201 → 63 pages). Item: map{hash, bsid},
  difficulty{difficulty 1/3/5/7/9, gameMode "SoloStandard", rawDifficulty}, realm{stars, rankedAt, …}.
- `GET /api/v2/realms/1/pp-curve` — {"curve": [[acc, multiplier], …] descending, 1.0 at 95%} (+ positiveModifierCurve).
  **pp = stars × 42.117 × curve(acc)** — verified 2026-11-22 on leaderboard 46809 (5.26★: 344.741 / 298.917 pp
  reproduced within 0.02%; listed `maxPP` = stars × 42.11).
- `GET /api/leaderboard/by-hash/{HASH}/info?difficulty={1|3|5|7|9}&gameMode=Solo{Characteristic}` → ranked, stars,
  maxPP, coverImage; 404 when the difficulty doesn't exist (D-060).
- Hash UPPERCASE. Rate limit 400/min (also 25/s, 100/10 s). No star/reweight history per map.
- **Verified 2026-11-22:** `GET /api/players?page=N` — global ranking, 50/page, `metadata.total` (37,096 ranked
  players), each {id, name, pp, rank, scoreStats{rankedPlayCount, averageRankedAccuracy}}; rank 1,000 = 12,174 pp.
  `GET /api/player/{id}/scores?sort=top&limit=100&page=N` — a player's scores best pp first, {score{pp, weight,
  baseScore, modifiedScore, modifiers "BE,…", multiplier, timeSet, …}, leaderboard{id, songHash, difficulty{difficulty,
  gameMode}, maxScore, stars, ranked, …}}; accuracy = baseScore / maxScore. Total pp = Σ pp_i × 0.965^i (exact on the
  rank-1 player: 22,029.77 over 1,233 ranked scores; `weight` field = 0.965^i).
  **Modifiers (same player, 1,233 scores, 2026-11-22):** pp = stars × 42.117 × curve(baseScore / maxScore), i.e. the
  unmodified accuracy: positive modifiers (FS, SF, DA, GN, PM, BE, SA, SC seen) give no pp bonus (pp/base = 1.0000,
  `positiveModifierCurve` unused by these scores); NF halves pp (pp/base 0.500). NO / NB / SS not seen: unverified.

## AccSaber (Reloaded) — acc-focused pool
Base `https://api.accsaber.com/v1`, spec at `/v1/api-docs`. Old `/ranked-maps` → 401 (SongSuggestCore broken).
- `/categories`: true_acc, standard_acc, tech_acc (+ overall). Curves embedded.
- **`/maps/difficulties/all`**: all 611 ranked diffs in one call, each with `complexity`, `categoryCode`,
  `ssLeaderboardId`, `blLeaderboardId` (cross-ids!). Standard characteristic only; includes Easy–Hard.
- Players: `/users/{steamId}`, `/users/{id}/scores`, `/users/{id}/skill` (per-category skillLevel 0–100),
  `/users/{id}/missing-maps`, `/users/{id}/categories/{cat}/ap-to-next`, `/users/{id}/ranking-history`.
- History: `/maps/{mapId}/complexity-history`, `/categories/{code}/reweights`.
- AP = curve(acc) × (complexity + 18) × 61; weighting logistic sigmoid (k 0.4, x1 15). Rate limit 400/60 s.
  **Verified 2026-11-22** on 4 live scores (exact): curve points from `GET /v1/curves` → "AccSaber Score Curve"
  (POINT_LOOKUP, 57 points {x, y}, `scale` 61, `shift` -18: AP = y × (complexity − shift) × scale); complexity from
  `/v1/maps/difficulties/all` (difficulty codes EASY … EXPERT_PLUS).
  **Weights verified 2026-11-22** (exact on 2 players: true_acc rank 1, 132 scores; tech_acc rank 41, 121 scores):
  category AP = Σ AP_i × w(i), scores by AP descending, i from 0; `/v1/categories` weightCurve LOGISTIC_SIGMOID
  k 0.4, y1 0.1, x1 15: S(i) = 1 / (1 + a·e^(k(i − x1))), w(i) = S(i) / S(0), a = (1 − y1) / (y1 − e^(−k·x1)) so that
  w(0) = 1 and w(x1) = y1. Each score also has `weightedAp`. Category leaderboards: `GET /v1/leaderboards/{code}`
  (paged; `ap`, `ranking`, `rankedPlays`; true_acc had 18,621 players).
- Backend reports channel "BETA" (v0.3.6) — expect churn.

## Cross-platform map identity
Canonical key (proposed): `UPPER(hash) | characteristic | difficulty` (Easy..ExpertPlus).
Store BL leaderboard id, SS leaderboard id, AccSaber diff uuid as attributes. Pitfalls: hash casing differs
(SS upper, others lower); map updates change hash (BeatSaver key stays); unranked/re-ranked → re-sync status;
exotic SS modes (`SoloStandard-PinkPlay_Controllable`).
Offline aggregators: SongDetailsCache (installed, SS+BL stars, ranked states), BeatSaver API (`stars`, `blStars`).

## BeatSaver curated maps (D-045, verified 2026-11-11)
`GET https://api.beatsaver.com/search/text/{page}?curated=true&sortOrder=Curated&pageSize=100` (0-based page, newest
curation first, **100 per page max**; `info.pages` is computed for 20 per page whatever `pageSize` is — stop at the
first empty page). 13,098 curated maps on 2026-11-11 (131 pages). Map: `curatedAt`, `curator`, `stats.score` (0..1),
`ranked`/`blRanked`, `versions[].hash`; diff: characteristic, difficulty, njs, nps, notes, `ne`/`me`/`chroma`/`cinema`
(**no Vivify flag**). No ratings: BeatLeader `/leaderboards/hash/{hash}` rates most curated maps (9/10 in a sample,
status 0), except modded ones (see Requirements above).
