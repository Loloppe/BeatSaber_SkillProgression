# Skill Progression

A Beat Saber PC plugin that turns ranked map pools and your scores into a structured, data-driven way to improve.

Instead of only showing statistics, Skill Progression tells you **where you stand, which style you are building, and
what to play next** — based on how thousands of BeatLeader players actually progressed.

> Status: in active development. Targets **Beat Saber 1.40.8**. Not released yet (see [Requirements](#requirements)).

## Features

- **Style mix** — the share of your top plays in each style (Accuracy, Midspeed, High Tech, Speed, Extreme), with the
  styles you have established marked ✓. Styles are defined from pass rating, tech rating and accuracy vs BeatLeader's
  predicted accuracy, and calibrated on the BeatLeader player population.
- **Milestones** — for the style you focus on, your position on a ladder of milestones taken from real player
  trajectories, labelled with the average global rank of players there. Progress counts both kinds of improvement:
  playing harder maps (*push*) and better accuracy on the maps you already play (*polish*).
- **Next up** — short-term targets ("Next clear target", "Next accuracy target") and a list of maps to play next, with
  a target accuracy for each. Maps you don't have are downloaded from BeatSaver; every map starts directly. Skip any
  recommendation; missing a target never costs anything.
- **Self-contained** — opening the plugin from the main menu takes over the menu (with the game's own gameplay setup
  panel for modifiers, player options, environments and colors). Finishing or quitting a map brings you back.
- **Explore** — fresh lists of maps to improve a score on or that you have never played, with an easier/harder slider
  and your personal expected accuracy.
- **Achievements & history** — milestones, completed steps, established styles, personal records, consistency,
  comebacks and BeatLeader / ScoreSaber / AccSaber rank and pp/AP thresholds, plus rank and milestone charts.
- **Platforms panel** — live BeatLeader, ScoreSaber and AccSaber rank and pp/AP.
- **Start fresh or import** — import your BeatLeader history, or start fresh after a long break; switchable later.

## Requirements

- Beat Saber **1.40.8** with BSIPA, SiraUtil, BeatSaberMarkupLanguage, SongCore.
- **BeatLeader** (score source and replay upload).
- **Reactive v2** (UI framework, `reactive-platform/reactive-mono`, branch `v2-dev`). On 1.40.8 the public BeatLeader
  release ships Reactive v1 under the same mod id, so a BeatLeader build using Reactive v2 is currently required —
  see [`tools/dev-env/README.md`](tools/dev-env/README.md).

## Repository layout

| Path | Contents |
|---|---|
| `src/SkillProgression.Core` | Progression engine, Unity-free (`netstandard2.0`): profiles, styles, milestones, objectives, recommendations, Explore, achievements |
| `src/SkillProgression.Data` | BeatLeader / ScoreSaber / AccSaber clients, model loader, local stores (`netstandard2.0`) |
| `src/SkillProgression.Plugin` | The Beat Saber plugin (`net48`, BSIPA + SiraUtil + Reactive v2) |
| `tools/SkillProgression.Tools` | Command-line tools (`player <BeatLeader id>`, `check-anchors`) |
| `training/` | Python scripts that explore the population data and build the progression model |
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
   Building the plugin copies `SkillProgression.dll` to `Plugins/` and `SkillProgression.Core.dll` /
   `SkillProgression.Data.dll` to `Libs/` of the game folder — close the game first.

The plugin reads its progression model from `UserData/SkillProgression/model.json.gz`.

## The progression model

The model (`model.json.gz`, ~4 MB) contains only aggregate data: destination thresholds, style-leaning parameters,
milestones per style, and per-map ratings, pp curves and peer popularity. It is built by
`training/build_model.py` from a local BeatLeader population snapshot, plus BeatLeader's public ratings for
ScoreSaber / AccSaber ranked maps (`training/fetch_pool_ratings.py`); the raw population data is not part of this
repository and is never published. See [`docs/PROGRESSION_DESIGN.md`](docs/PROGRESSION_DESIGN.md).

## Data stored on your computer

Everything lives in `UserData/SkillProgression/` (a few MB per year): your choices, a cache of your BeatLeader scores,
a log of plays started from the plugin, achievements and progression history.

## Documentation

- [`docs/PROGRESSION_DESIGN.md`](docs/PROGRESSION_DESIGN.md) — how styles, milestones and recommendations are defined
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — modules and data flow
- [`docs/DECISIONS.md`](docs/DECISIONS.md) — decision log
- [`docs/API_INTEGRATIONS.md`](docs/API_INTEGRATIONS.md) — BeatLeader / ScoreSaber / AccSaber API notes
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — status and next steps

## Acknowledgements

Built on data from [BeatLeader](https://beatleader.com), [ScoreSaber](https://scoresaber.com) and
[AccSaber](https://accsaber.com), maps from [BeatSaver](https://beatsaver.com), and the
[Reactive](https://github.com/reactive-platform) UI framework.

## License

[MIT](LICENSE)
