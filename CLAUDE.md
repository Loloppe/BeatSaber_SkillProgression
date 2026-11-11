# Skill Progression — Claude Code project instructions

Beat Saber PC plugin that turns ranked map pools (BeatLeader, ScoreSaber, AccSaber) and the player's
BeatLeader scores into a structured, in-game skill progression experience. UI uses Reactive Platform.

## Start every session by reading
- `docs/ROADMAP.md` — current phase and next steps
- `docs/DECISIONS.md` — decisions made and open questions (never re-decide silently)
- `docs/TECH_FINDINGS.md` — Beat Saber / Reactive / tooling research
- `docs/API_INTEGRATIONS.md` — BeatLeader / ScoreSaber / AccSaber API facts and limits
- `docs/PROGRESSION_DESIGN.md` — skill model / progression design (draft + decisions)
- `docs/ARCHITECTURE.md` — module responsibilities, Core data flow, model.json contract

## Working agreement (from the user)
- Collaborative: discuss architecture, data-model, UX, and long-term-maintainability decisions with the
  user before committing. Present options, trade-offs, a recommendation, then ask — one question at a time.
- Distinguish: user requirements vs. Claude recommendations vs. optional ideas.
- Investigate before guessing; never implement API functionality that hasn't been verified to exist.
- Never overwrite/delete working code or do destructive refactors without explicit approval.
- No unnecessary dependencies. No silent hacks around architectural problems.
- Progression logic must be testable without Beat Saber; never couple it to Unity/UI.
- Keep the project buildable at each meaningful step; small reviewable steps.
- At the end of each session, update the relevant docs (ROADMAP, DECISIONS, etc.).
- **Never commit** (user, 2026-11-10): the user makes all git commits. Leave changes uncommitted and say what changed.
  Earlier history (51 commits) is backed up in `data/history-backup-2026-11-10.bundle` (local, not in git).

## Fixed facts
- Target game version: **Beat Saber 1.40.8** (Unity 2022.3.33f1, Mono .NET 4.x profile).
  Dev instance: the 1.40.8 game folder set as `BeatSaberDir` in `src/SkillProgression.Plugin/SkillProgression.Plugin.csproj.user`
  (not committed; a newer Steam install is NOT the target).
- Layout (D-014): `src/SkillProgression.Core` (pure logic, no I/O, netstandard2.0), `src/SkillProgression.Data`
  (API clients, persistence), `src/SkillProgression.Plugin` (net48, BSIPA/SiraUtil/Reactive v2), `tools/SkillProgression.Tools`
  (net10 CLI), `training/` (Python), `tests/` (xUnit), `data/` (gitignored local data — never commit).
- Build/test: `dotnet build SkillProgression.slnx`, `dotnet test SkillProgression.slnx` (SDK pinned by global.json).
  The Plugin project needs `src/SkillProgression.Plugin/SkillProgression.Plugin.csproj.user` with `<BeatSaberDir>`
  and **copies into the game instance on build** (Plugins/SkillProgression.dll, with Core/Data sources compiled in; D-046) —
  the game must be closed. Reactive v2 needs C# 14 (.NET 10 SDK) + `Reactive.Compiler` NuGet.
- Feature extraction & player inference exist ONLY in Core (shared by CLI and plugin) — never reimplement in Python.
- Raw attempts / per-player population records must never be committed or published (D-007).
- Model: `cd training && .venv/Scripts/python fetch_pool_ratings.py` (SS/AccSaber maps' BL ratings, cached; D-041)
  and `.venv/Scripts/python fetch_curated.py` (BeatSaver curated maps' BL ratings, cached; ~1 h first run; D-045) then `.venv/Scripts/python build_model.py` → `data/model/model.json.gz` (needs explorations 02–06 outputs in data/explore). CLI: `dotnet run --project tools/SkillProgression.Tools -- player <blId>` and
  `-- check-anchors` (must stay 11/11 after any rules/model change).
- Dev environment for Reactive v2 + BeatLeader: installed in the 1.40.8 instance (D-016); see `tools/dev-env/README.md`.
- Population DB (D-015): `../portaBLerebalancer2/wwwroot/db-20261104211323-3944.db` — open read-only.
- Player-specific data (anchor/review Steam IDs) lives only in `data/anchors.json` (gitignored). Never put player
  IDs or local paths in tracked files (user, 2026-11-10).
