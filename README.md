# Skill Progression

A Beat Saber PC plugin that turns ranked map pools, BeatSaver's curated maps and your scores into a structured,
data-driven way to improve.

Instead of only showing statistics, Skill Progression tells you **where you stand, which style you are building, and
what to play next** — based on how thousands of BeatLeader players actually progressed.

> Status: in active development. Targets **Beat Saber 1.40.8**. Not released yet: it will ship as a .zip on a GitHub
> release once its requirements are publicly available (see [Requirements](#requirements)).

## Features

- **Style mix** — the share of your top plays in each style (Accuracy, Midspeed, High Tech, Speed, Extreme), with the
  styles you have established marked ✓. Styles are defined from pass rating, tech rating and accuracy vs BeatLeader's
  predicted accuracy, and calibrated on the BeatLeader player population.
- **Milestones** — for the style you focus on, your position on a ladder of milestones taken from real player
  trajectories, labelled with the average global rank of players there. Progress counts both kinds of improvement:
  playing harder maps (*push*) and better accuracy on the maps you already play (*polish*).
- **Next up** — short-term targets ("Next clear target", "Next accuracy target") and a list of maps to play next, with
  a target accuracy for each. Maps you don't have are downloaded from BeatSaver; every map starts directly. Skip any
  recommendation; missing a target never costs anything, and a map missed three times moves down the list. Step sizes
  adapt to how your recent plays went: smaller after fails, larger after a run of reached targets.
- **Map pools** — recommendations come from BeatLeader, ScoreSaber or AccSaber ranked maps, or any of them (AccSaber
  for the Accuracy style only). Progress is measured the same way whatever the pool.
- **Self-contained** — opening the plugin from the main menu takes over the menu (with the game's own gameplay setup
  panel for modifiers, player options, environments and colors). Finishing or quitting a map brings you back.
- **Explore** — fresh lists of maps to improve a score on or that you have never played, with your personal expected
  accuracy. A slider starts at the difficulty Training works at for the style and goes down to half of it, or a few
  steps harder. Besides the ranked pools, Explore can use **BeatSaver's curated maps** (those
  BeatLeader rates; plays on them don't count toward progression).
- **Achievements & history** — milestones, completed steps, established styles, personal records, consistency,
  comebacks and BeatLeader / ScoreSaber / AccSaber rank and pp/AP thresholds, plus rank and milestone charts.
- **Platforms panel** — live BeatLeader, ScoreSaber and AccSaber rank and pp/AP.
- **Start fresh or import** — import your BeatLeader history, or start fresh after a long break; switchable later.
  After a fresh start, a short calibration (one map at a time) finds your current level on the style you focus on.
- **Safe starts** — a map that needs a mod you don't have (Noodle Extensions, Mapping Extensions, Vivify, …) is not
  started; you get a message naming the missing mods instead.

## Requirements

- Beat Saber **1.40.8** with BSIPA, SiraUtil, BeatSaberMarkupLanguage, SongCore, and the System.IO.Compression /
  System.IO.Compression.FileSystem libraries (used to unzip BeatSaver downloads).
- An internet connection for BeatLeader / ScoreSaber / AccSaber data and BeatSaver downloads (cached scores are used
  when offline).
- **BeatLeader** (score source and replay upload).
- **Reactive v2** (UI framework, `reactive-platform/reactive-mono`, branch `v2-dev`). On 1.40.8 the public BeatLeader
  release ships Reactive v1 under the same mod id, so a BeatLeader build using Reactive v2 is currently required —
  see [`tools/dev-env/README.md`](tools/dev-env/README.md).

## Repository layout

| Path | Contents |
|---|---|
| `src/SkillProgression.Core` | Progression engine, Unity-free (`netstandard2.0`): profiles, styles, milestones, objectives, recommendations, Explore, achievements |
| `src/SkillProgression.Data` | BeatLeader / ScoreSaber / AccSaber clients, model loader, local stores (`netstandard2.0`) |
| `src/SkillProgression.Plugin` | The Beat Saber plugin (`net48`, BSIPA + SiraUtil + Reactive v2); Core and Data are compiled into it |
| `tools/SkillProgression.Tools` | Command-line tools (`player <BeatLeader id>`, `check-anchors`) |
| `training/` | Python scripts that explore the population data, fetch map ratings and build the progression model |
| `tests/` | xUnit tests for Core and Data |
| `docs/` | Design, architecture, API notes and decision log |

## Building

Requirements: .NET 10 SDK (pinned in `global.json`; needed by the Reactive v2 source generator), a Beat Saber 1.40.8
installation with the mods above.

1. Create `src/SkillProgression.Plugin/SkillProgression.Plugin.csproj.user` pointing at your game folder:
   ```xml
   <Project>
     <PropertyGroup>
       <BeatSaberDir>C:\Path\To\Beat Saber</BeatSaberDir>
     </PropertyGroup>
   </Project>
   ```
2. Build and test:
   ```
   dotnet build SkillProgression.slnx
   dotnet test SkillProgression.slnx
   ```
   Building the plugin copies `SkillProgression.dll` to `Plugins/` of the game folder — close the game first. Core and
   Data are compiled into that one DLL; their own projects only serve the tests and the command-line tools.
3. Release zip: `dotnet build src/SkillProgression.Plugin/SkillProgression.Plugin.csproj -c Release` writes
   `src/SkillProgression.Plugin/bin/Release/net48/zip/SkillProgression-<version>-bs1.40.8-<commit>.zip` with
   `Plugins/SkillProgression.dll` (+ `.pdb`) and `UserData/SkillProgression/model.json.gz` (from `data/model/`; the
   build warns when the model is missing).

The plugin reads its progression model from `UserData/SkillProgression/model.json.gz` (the build copies the local
`data/model/model.json.gz` there).

## The progression model

The model (`model.json.gz`, a few MB) contains only aggregate data: destination thresholds, style-leaning parameters,
milestones per style, and per-map ratings, pp curves, peer popularity and curated flags. Build it from `training/`:

```
.venv/Scripts/python fetch_pool_ratings.py   # BeatLeader ratings of ScoreSaber / AccSaber ranked maps (cached)
.venv/Scripts/python fetch_curated.py        # BeatSaver curated maps + their BeatLeader ratings (cached; ~1 h first run)
.venv/Scripts/python build_model.py          # -> data/model/model.json.gz
```

`build_model.py` needs a local BeatLeader population snapshot and the outputs of the exploration scripts; the raw
population data is not part of this repository and is never published. After a model change,
`dotnet run --project tools/SkillProgression.Tools -- check-anchors` must still match every anchor player. See
[`docs/PROGRESSION_DESIGN.md`](docs/PROGRESSION_DESIGN.md).

## Data stored on your computer

Everything lives in `UserData/SkillProgression/` (a few MB per year): your choices, caches of your BeatLeader scores
(ranked, and unranked ones once you use Explore's curated maps), a log of plays started from the plugin, achievements
and progression history.

## Documentation

- [`docs/PROGRESSION_DESIGN.md`](docs/PROGRESSION_DESIGN.md) — how styles, milestones and recommendations are defined
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — modules and data flow
- [`docs/DECISIONS.md`](docs/DECISIONS.md) — decision log
- [`docs/API_INTEGRATIONS.md`](docs/API_INTEGRATIONS.md) — BeatLeader / ScoreSaber / AccSaber / BeatSaver API notes
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — status and next steps

## Acknowledgements

Built on data from [BeatLeader](https://beatleader.com), [ScoreSaber](https://scoresaber.com) and
[AccSaber](https://accsaber.com), maps from [BeatSaver](https://beatsaver.com), and the
[Reactive](https://github.com/reactive-platform) UI framework.

## License

[MIT](LICENSE)
