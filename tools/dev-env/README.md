# Dev environment: BeatLeader + Reactive v2 on Beat Saber 1.40.8

Needed because Reactive v2 (D-002) cannot coexist with BeatLeader's Reactive v1 (see docs/TECH_FINDINGS.md).
Installed into the existing 1.40.8 instance (D-016). Backup/restore: `data/dev-env/RESTORE.md`.

## Sources
- BeatLeader: `https://github.com/BeatLeader/beatleader-mod` branch `nsgolova/reactive2` @ `06ce7275`
  + `reactive2-1408-code.patch` (this folder; 23 files, mostly upstream `origin/1408support` reverts).
  The patch excludes the binary asset bundle: take `Source/3_AssetBundle/...bundle` from `origin/1408support`
  (Unity 2022.3 bundle). Full patch incl. bundle (30 MB, not in git): `data/dev-env/reactive2-1408.patch`.
- Reactive v2: `https://github.com/reactive-platform/reactive-mono` branch `v2-dev` @ `5e6a0a0` (2026-11-10).
  If BL's v2 UI misbehaves, try ~`5942134` (what BL PR #216 was written against).

## Build (copy-to-game disabled)
`dotnet build -c Release -p:BeatSaberDir=<refs dir> -p:DisableCopyToPlugins=true -p:DisableCopyToGame=true -p:DisableZipRelease=true`
The refs dir = copy of the 1.40.8 Managed/Libs/Plugins DLLs with Reactive v2 DLLs swapped in and
`Plugins/Reactive.BeatSaber.Components.dll` removed.

## Install into the dev instance (back up first)
- Replace `Plugins/BeatLeader.dll`, `Libs/Reactive.dll`, `Libs/Reactive.Components.dll`, `Plugins/Reactive.BeatSaber.dll`.
- Delete stale `Plugins/Reactive.BeatSaber.Components.dll` (Reactive.BeatSaberSDK 0.2.0).
- Yoga 3.2.1 (`Plugins/Yoga.manifest`, `Libs/Native/yoga.dll`) already satisfies Reactive v2.

Status 2026-11-10: compiles with 0 errors; **not yet run in-game**.

## Local backups (data/dev-env, not in git)
- `beatleader-r2-1408.bundle` — the BeatLeader branch `r2-1408` (commit 8568ac4c on top of `origin/nsgolova/reactive2`),
  including the 2022.3 asset bundle. Restore: `git clone https://github.com/BeatLeader/beatleader-mod.git`, then
  `git fetch <path>/beatleader-r2-1408.bundle r2-1408:r2-1408`.
- `reactive2-1408.patch` — same changes as one patch file (with the binary bundle).
- `build/` — the built Reactive v2 (reactive-mono `v2-dev` @ 5e6a0a0, public) and BeatLeader DLLs installed in the dev instance.
- `backup-1.40.8-2026-11-10/` + `RESTORE.md` — original files of the 1.40.8 instance.
