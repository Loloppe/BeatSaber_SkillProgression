# Technical findings — Beat Saber 1.40.8, Reactive, tooling (as of 2026-11-10)

## Game / mod environment (1.40.8 instance)
Unity 2022.3.33f1; Mono .NET 4.x profile (net472/net48 or netstandard2.0 assemblies load).
BSIPA 4.3.6, SiraUtil 3.2.1, BSML 1.12.5, SongCore 3.15.3, SongDetailsCache 1.4.0, BeatSaverSharp 3.4.5,
BeatSaberPlaylistsLib 1.7.2, PlaylistManager 1.7.3, BeatLeader 0.10.666, ScoreSaber 3.3.17, Newtonsoft 13.0.x.
BeatMods API v3: `https://beatmods.com/api/v3/mods?gameName=beatsaber&gameVersion=1.40.8&status=verified`.

## Reactive Platform
- Only active repo: `reactive-platform/reactive-mono`. `reactive-ui` / `reactive-sdk` are archived (404 on GitHub).
  Local clones of those two are dead; local `reactive-mono` is behind (pull; use branch `1408support` for 1.40.8).
- Branches: `master` = **v1 (1.0.0)**; `1408support` = 1.40.8 backport (what's installed);
  `v2-dev` = **breaking v2 rewrite**, unreleased, same mod id `Reactive`, version 2.0.0.
- On 1.40.8 Reactive 1.0.0 + Yoga 3.2.1 are **not on BeatMods**; they ship inside BeatLeader's GitHub zip.
- The 1.40.8 instance also contains stale `Plugins/Reactive.BeatSaber.Components.dll` (id Reactive.BeatSaberSDK 0.2.0)
  — duplicate namespaces; never reference it.
- Plugin references (Private=False): `Libs/Reactive.dll`, `Libs/Reactive.Components.dll`, `Plugins/Reactive.BeatSaber.dll`;
  manifest `"Reactive": "^1.0.0"`. Never bundle the DLLs.
- v1 model: `ReactiveComponent` (plain C# object, `Construct()` → GameObject, `.Use(parent)`), Yoga flexbox
  (`AsFlexGroup`/`AsFlexItem`), `ObservableValue<T>`/`Remember`, `AnimatedValue<T>`/`RememberAnimated`,
  manual binding (`.Animate`, `WithListener`, `.Bind(ref)`, `.Export(out)`).
- v2 model: `State<T>`, `.Map`, derived states, Roslyn source generator (`Reactive.Compiler` NuGet) with `sProp` bindings,
  frame-batched `ReactiveScheduler`; requires LangVersion 14. Docs site (reactiveui.beatleader.com) describes **v2**.
- **No navigation** → use HMUI FlowCoordinator/ViewController + SiraUtil Zenject + BSML MenuButtons.
  Components aren't injected; pass services via `Setup(...)`.
- Components (v1): Layout, Label, Image, buttons, SegmentedControl+KeyedContainer (tabs), ListView,
  virtualized Table, ScrollArea, modals/Dialog, Slider, Toggle, Dropdown, InputField+Keyboard, Spinner, LoadingContainer.
  **Missing: progress bar, charts/graphs, tooltips** → build ourselves (charts = custom `UnityEngine.UI.Graphic`
  with `OnPopulateMesh`, as BeatLeader's accuracy graph does).
- Threading: main thread only; no dispatcher.
- Preview outside the game: only a v2 WASM web playground (stub UnityEngine). Nothing for v1 → FPFC in-game testing.
- License: MIT on v2-dev only; v1/master/1408support have no LICENSE file.
- Reference code: BeatLeader mod `Source/8_UI` (v1, use `origin/1408support`), `reactive-showcase` (v2).

## Project setup (recommended, not yet approved)
- SDK-style csproj, net48, LangVersion latest, PolySharp; BeatSaberModdingTools.Tasks 2.0.0-beta7;
  BepInEx.AssemblyPublicizer.MSBuild for game internals; BeatSaberDir in `.csproj.user`.
- Split: `SkillProgression.Core` (netstandard2.0, no Unity) / `SkillProgression` (net48 plugin) /
  `SkillProgression.Tests` (net8). Precedent: BeatSaberPlaylistsLib.
- SiraUtil Zenjector: App location for services, Menu for FlowCoordinator/ViewControllers/menu button.
- HTTP: `HttpClient` works on 1.40.8 Mono (BL mod uses it). Core uses `ConfigureAwait(false)`; plugin layer
  marshals to main thread.
- Persistence: JSON (Newtonsoft) under `UserData/SkillProgression/`, atomic writes. No native sqlite3 present.
  DPAPI (`System.Security.ProtectedData`) likely available if a token is ever needed (unverified at runtime).

## Interop
- Downloaded check: `SongCore.Collections.songWithHashPresent(hash)`, `Loader.GetLevelByHash`.
- Download: BeatSaver API / BeatSaverSharp; then `Loader.Instance.RefreshSongs(false)`.
- Playlist: BeatSaberPlaylistsLib `PlaylistManager.DefaultManager.CreateChildManager(...)` (soft dependency).
- Jump to level: BetterSongSearch `UI/SelectedSongView.cs` pattern (LevelSelectionFlowCoordinator.State via
  reflection + SoloFreePlayFlowCoordinator.Setup) — fragile, version-specific.

## Reactive v2 on 1.40.8 (researched 2026-11-10, after D-002)
- `v2-dev` (5e6a0a0) **compiles against 1.40.8 unchanged** (AppInit patch already merged). Runtime in-game untested.
- **Cannot coexist with BeatLeader's v1**: same BSIPA id `Reactive`, same file paths (Libs/Reactive.dll,
  Libs/Reactive.Components.dll, Plugins/Reactive.BeatSaber.dll), same assembly names (Reactive 1.0.0.0),
  same embedded asset bundle, same Harmony id. Installing v2 disables BeatLeader (`Reactive ^1.0.0`).
- BeatLeader's v2 migration = PR #216 (`nsgolova/reactive2`), open, targets 1.42. No 1.40.8 v2 build announced.
  BL 1.40.8 zips are built from unpublished branches; BL still ships 1.40.8 builds (v0.10.666, 2026-10-31).
- Options: (a) wait for official v2 + BL 1.40.8 v2 build; (b) develop now against self-built v2 in a separate
  dev instance / with mocked BL data, release when (a) happens; (c) rename/embed (MIT allows; blockers: duplicate
  asset bundle load, Harmony id clash, generator matches qualified names) — emergency only; (d) ask Hermanest.
  Research recommendation: (b)+(d).
- Toolchain: `Reactive.Compiler` 1.0.0 NuGet (Roslyn 5 → .NET 10 SDK / VS 2026+), C# 14 (`extension` blocks,
  `field`), PolySharp/IsExternalInit on net48. Template: reactive-showcase `ReactiveShowcase.csproj`.
- v2 components: Repeater, virtualized Table (CellContext), ScrollArea, Modal/Dialog, AnimatedState/AnimationPlayer,
  Bs* controls. No chart/progress bar. Hosted in plain HMUI.ViewController via `new Root().Use(transform)`.
- Scratch build artifacts: `%TEMP%
v2research\`.

## Self-contained mode (D-006) on 1.40.8 — feasible, verified against decompiled binaries
- Start: `MenuTransitionsHelper.StartStandardLevel("Solo", key, level, ...)` (overload without level data),
  args from `PlayerDataModel.playerData`; must be main thread; verify `BeatmapLevelsModel.GetBeatmapLevel(id) != null`.
  gameMode **must be "Solo"** for BL/SS submission.
- Return: menu scene stays loaded under gameplay → our FlowCoordinator is still on top after `levelFinishedCallback`.
  Restart path calls `levelRestartedCallback` only. Outcome from `levelEndStateType` + `levelEndAction`.
- We must update local stats ourselves (`LevelCompletionResultsHelper.ProcessScore`, overall stats, Save),
  skipping practice and `SiraLevelCompletionResults.ShouldSubmitScores == false`.
- **BeatLeader only records if `RecorderUtils.shouldRecord`** (set by its patches on the level-select play button)
  → before each start invoke `BeatLeader.Utils.RecorderUtils.OnActionButtonWasPressed` via reflection
  (BeatSaberPlus does this). ScoreSaber submits Solo starts without extra work.
- Hiding: presenting our FlowCoordinator with null side screens hides the main menu, BSML mods panel, BL noticeboard.
  Third-party FloatingScreens are not covered (opt-in hiding, fragile). Settings-triggered game restart drops our flow.
- Download: BeatSaver CDN zip → CustomLevels → `SongCore.Loader.Instance.RefreshSongs(false)` → wait for event →
  verify level → start. Check map requirements against `SongCore.CapabilityRegistry`.
- Drills: `PracticeSettings(startTime, speed)`; section end needs our own gameplay component (unverified).
  Speed modifiers via `gameplayModifiers.CopyWith(...)`.

## BeatLeader `nsgolova/reactive2` ported to 1.40.8 (assessed 2026-11-10, temp only)
- With a small patch (23 files, +187/−167, mostly upstream's own `1408support` reverts + asset-bundle swap to the
  Unity 2022.3 bundle), reactive2 **compiles with 0 errors** against 1.40.8 + Reactive v2 (`v2-dev` 5e6a0a0).
  Not run in-game yet. Hand-written bits: ReplayerCameraController (URP removal), Billboard (`AnimationTiming`).
- Artifacts (temp, may be wiped): `%TEMP%lreactive2\` — `reactive2-1408.patch` (full, with bundle),
  `reactive2-1408-code.patch`, clone `bl\` branch `r2-1408`, built BeatLeader.dll, Reactive v2 build in `rm-v2\`.
  **Copy the patches into the repo (e.g. `tools/dev-env/`) before %TEMP% is cleaned.**
- The user's uncommitted local beatleader-mod diff is a **1.45.x** port (close to upstream `origin/1.45.3`), not 1.40.8.
- Installed BL 0.10.666 (1.40.8) is from an unpublished build of `halloween-release-1420`; a reactive2-based dev
  build would lack its extra features (better-modifiers, eye tracking, Vivify rewind) — irrelevant for our dev use.
- To run: replace BeatLeader.dll, Libs/Reactive*.dll, Plugins/Reactive.BeatSaber.dll; delete stale
  Plugins/Reactive.BeatSaber.Components.dll; Yoga 3.2.1 already OK. Back up first. Prefer a **separate copy of the
  1.40.8 instance** for v2 development.
- If the BL v2 UI misbehaves: build Reactive at ~Nov 4 commit (`5942134`) which the PR was written against.
- **Smoke test 2026-11-10 (user, FPFC):** game loads; BSIPA reports Reactive 2.0.0 + BeatLeader 0.10.0; BeatLeader UI
  works. Only notable exception (`HMUI.CurvedCanvasSettings.Start` NRE) is pre-existing (present in every 2026-11-08
  log with Reactive 1.0.0). Warnings "developed for 1.42.0" for Reactive/Yoga are expected. **Score submission not yet tested.**
- **Manifest pitfall (2026-11-10):** SongCore on 1.40.8 reports `3.15.3-bs1.40.8-redux` — a semver *pre-release*,
  which is lower than `3.15.3`. `"SongCore": "^3.15.3"` therefore fails ("missing dependency; ignoring").
  Use `^3.15.2` (as BeatLeader does). Versions with only build metadata (`+bs.1.40.0`, SiraUtil/BSML) are fine.
- **Reactive v2 pitfalls (in-game review 2026-11-10):** (1) a component's `Construct()` runs inside its constructor,
  so object-initializer inputs arrive *after* the UI is built — expose inputs as settable properties (the generator then
  provides `sX = state` bindings) instead of init-only properties read in Construct. (2) Reactive images draw through
  HMUI `ImageView`, which ignores Unity's `Image.Type.Filled` / `fillAmount` → resize a child for progress bars.
  (3) Generated `s`-bindings only exist inside `Construct` or `[StateGen]` members, and need concrete state types.
  (4) `WithRectExpand()` (namespace `Reactive`) makes a root view fill its ViewController.
