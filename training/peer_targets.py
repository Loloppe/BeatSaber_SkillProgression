"""Peer-based accuracy targets (D-058): per map, the accuracy of players at each level, by style x difficulty band.

Cells: per style (Destination), the style-wide cell "<Destination>" and 3 difficulty bands "<Destination>|<b>" (equal
map counts along the style's axis); AccSaber tracks (D-061) likewise, on their category's maps along complexity. In each cell a robust additive fit over comparable PBs,
acc[p, m] ~ Ease[m] + level[p] (alternating medians, players with >= MIN_FIT maps in the cell, levels centred on the
cell's median player), gives every population player a level. Scores count with no modifier or only
NEUTRAL_MODIFIERS (same rule as Core's PeerTargets.Comparable). Per map, the model stores at LEVEL_QUANTILES of the
cell's levels the median of (acc - level + grid level) over players near that grid level (window grown until >=
MIN_BAND players): "players at your level get this here". Core estimates the player's level per cell from their own
PBs (median of acc - Ease) and reads the curve; a band with < MIN_PLAYER_MAPS of the player's maps uses the
style-wide cell. Aggregates only (D-007): every value is a median of >= MIN_BAND players.

Scores: population DB (BL-ranked maps) + fetch_peer_scores.py cache (SS / AccSaber maps not BL-ranked).
"""
import json
import os

import numpy as np
import pandas as pd

from common import out_dir
from profiles import DESTINATIONS, path_plays

ACCSABER_TRACKS = {"true_acc": "True Acc", "standard_acc": "Standard Acc", "tech_acc": "Tech Acc"}  # Core AccSaberTracks

MIN_FIT = 20          # population player's maps in a cell to be part of its fit
MIN_BAND = 8          # players behind every stored value
MIN_PLAYER_MAPS = 10  # the player's maps in a band before its own band level is used (Core)
BANDS = 3
WINDOWS = (0.0005, 0.001, 0.0015, 0.002, 0.003, 0.005, 0.01)  # half-widths in accuracy (0..1)
LEVEL_QUANTILES = (0.02, 0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.85, 0.9, 0.95, 0.97, 0.98, 0.99, 0.995, 0.999)
FIT_ITERATIONS = 6
# Neither change the song speed nor make the map easier (user, D-058): Old Dots, Pro Mode, Disappearing Arrows,
# Ghost Notes, Battery Energy. Mirrors Core PeerTargets.NeutralModifiers.
NEUTRAL_MODIFIERS = {"OD", "PM", "DA", "GN", "BE"}


def comparable(mods) -> bool:
    return not mods or all(m.strip().upper() in NEUTRAL_MODIFIERS for m in mods.split(",") if m.strip())


def population_scores(con, maps) -> pd.DataFrame:
    """Comparable PBs (player, map index into `maps`, accuracy 0..1)."""
    index = {(m["Hash"], m["Difficulty"]): i for i, m in enumerate(maps)}
    lbs = pd.read_sql_query("select Id, Hash, DifficultyName, ModeName from Leaderboards", con)
    lbs = lbs[lbs.ModeName == "Standard"]
    lb_map = {lb: index.get((h.upper(), d), -1) for lb, h, d in zip(lbs.Id, lbs.Hash, lbs.DifficultyName)}
    db = pd.read_sql_query("select PlayerId, LeaderboardId, Accuracy, Modifiers from Scores", con)
    db = db[db.Modifiers.map(comparable)]
    db["map"] = db.LeaderboardId.map(lb_map).fillna(-1).astype(int)
    parts = [db.loc[db["map"] >= 0, ["PlayerId", "map", "Accuracy"]]]

    idx_path = os.path.join(out_dir("12_peer_scores"), "index.json")
    if os.path.exists(idx_path):
        raw = out_dir(os.path.join("12_peer_scores", "raw"))
        rows = []
        for e in json.load(open(idx_path, encoding="utf-8")):
            i = index.get((e["Hash"], e["Difficulty"]), -1)
            path = os.path.join(raw, f"{e['LeaderboardId']}.json")
            if i < 0 or maps[i]["RankedBeatLeader"] or not os.path.exists(path):
                continue
            rows += [(p, i, a) for p, a, mods, _ in json.load(open(path, encoding="utf-8")) if comparable(mods)]
        parts.append(pd.DataFrame(rows, columns=["PlayerId", "map", "Accuracy"]))
    else:
        print("no peer scores for SS / AccSaber maps (run fetch_peer_scores.py) - BeatLeader-ranked maps only")
    s = pd.concat(parts, ignore_index=True).drop_duplicates(["PlayerId", "map"])
    return s.rename(columns={"PlayerId": "player", "Accuracy": "acc"})


def cells(maps, played, edges=None) -> list[tuple[str, str, int, np.ndarray]]:
    """[(cell id, destination, band or -1, map indices)] from the maps' no-modifier ratings. `edges` (optional dict)
    receives each style's / track's band edges on its axis (update_model.py places new maps with them)."""
    r = pd.DataFrame([{"i": i, "PassRating": m["Ratings"][""]["Pass"], "TechRating": m["Ratings"][""]["Tech"],
                       "Stars": m["Ratings"][""]["Stars"]} for i, m in enumerate(maps) if i in played])
    r["Complexity"] = [maps[i].get("AccSaberComplexity", 0) for i in r.i]
    r["Category"] = [maps[i].get("AccSaberCategory", "") for i in r.i]
    groups = [(path, *path_plays(r, path)) for path in DESTINATIONS]
    groups += [(track, r[r.Category == code], "Complexity") for code, track in ACCSABER_TRACKS.items()]
    out = []
    for path, sub, axis in groups:
        if sub.empty:
            continue
        out.append((path, path, -1, sub.i.to_numpy()))
        e = np.quantile(sub[axis], np.linspace(0, 1, BANDS + 1)[1:-1])
        if edges is not None:
            edges[path] = [float(x) for x in e]
        band = np.searchsorted(e, sub[axis].to_numpy(), side="right")
        for b in range(BANDS):
            out.append((f"{path}|{b}", path, b, sub.i.to_numpy()[band == b]))
    return out


def fit_cell(s: pd.DataFrame):
    """Additive fit on one cell's scores -> (ease by map, level by player) or None."""
    n = s.groupby("player").size()
    s = s[s.player.isin(n.index[n >= MIN_FIT])]
    if s.empty:
        return None
    ease = s.groupby("map").acc.median()
    for _ in range(FIT_ITERATIONS):
        level = (s.acc - s["map"].map(ease)).groupby(s.player).median()
        level -= level.median()
        ease = (s.acc - s.player.map(level)).groupby(s["map"]).median()
    return ease, level, s


def band_value(lv: np.ndarray, ac: np.ndarray, g: float) -> float | None:
    for h in WINDOWS:
        lo, hi = np.searchsorted(lv, g - h), np.searchsorted(lv, g + h, side="right")
        if hi - lo >= MIN_BAND:
            return float(np.median(ac[lo:hi] - lv[lo:hi] + g))
    return None


def build(con, maps, report_players=()):
    """Adds MapEntry.PeerTargets to `maps` in place; returns the model's PeerTargets section.
    Fitted levels of `report_players` go to data/explore/12_peer_scores/fitted_levels.json (local only) for the CLI
    consistency check."""
    scores = population_scores(con, maps)
    print(f"peer targets: {len(scores):,} comparable PBs, {scores.player.nunique():,} players, {scores['map'].nunique():,} maps")
    by_map = {m: g for m, g in scores.groupby("map")}
    section, fitted, edges, levels = {"MinPlayerMaps": MIN_PLAYER_MAPS, "Cells": []}, {}, {}, []
    for cell_id, dest, band, idx in cells(maps, set(by_map), edges):
        s = pd.concat([by_map[i] for i in idx]) if len(idx) else scores.iloc[:0]
        fit = fit_cell(s)
        if fit is None:
            continue
        ease, level, s = fit
        grid = np.quantile(level.to_numpy(), LEVEL_QUANTILES)
        s = s.assign(l=s.player.map(level)).sort_values("l")
        stored = 0
        for m, g in s.groupby("map"):
            lv, ac = g.l.to_numpy(), g.acc.to_numpy()
            pts = [(float(x), v) for x in grid if (v := band_value(lv, ac, x)) is not None]
            maps[m].setdefault("PeerTargets", {})[cell_id] = {
                "Ease": round(float(ease[m]), 5), "Levels": [round(x, 5) for x, _ in pts], "Values": [round(v, 5) for _, v in pts]}
            stored += 1
        section["Cells"].append({"Id": cell_id, "Destination": dest, "Band": band, "Players": int(level.size), "Maps": stored})
        levels.append(pd.DataFrame({"cell": cell_id, "player": level.index.astype(str), "level": level.to_numpy()}))
        for p in report_players:
            if p in level.index:
                fitted.setdefault(p, {})[cell_id] = round(float(level[p]), 6)
        print(f"  {cell_id:12} players {level.size:6,}  maps {stored:5,}")
    json.dump(fitted, open(os.path.join(out_dir("12_peer_scores"), "fitted_levels.json"), "w", encoding="utf-8"), indent=1)
    # Every population player's fitted level per cell and the band edges (local only, D-007): update_model.py adds
    # peer curves for maps ranked after this build without refitting.
    pd.concat(levels, ignore_index=True).to_csv(os.path.join(out_dir("12_peer_scores"), "levels.csv.gz"), index=False)
    json.dump(edges, open(os.path.join(out_dir("12_peer_scores"), "band_edges.json"), "w", encoding="utf-8"), indent=1)
    return section


def map_curve(scores: pd.DataFrame, level: pd.Series, grid) -> dict | None:
    """One map's peer curve in a cell from its comparable PBs (player, acc) and the cell's fitted levels — the same
    per-map step as build() (ease = median of acc - level, values via band_value); None under MIN_BAND players."""
    s = scores.assign(l=scores.player.map(level)).dropna(subset=["l"]).sort_values("l")
    if len(s) < MIN_BAND:
        return None
    lv, ac = s.l.to_numpy(), s.acc.to_numpy()
    pts = [(float(x), v) for x in grid if (v := band_value(lv, ac, x)) is not None]
    return {"Ease": round(float(np.median(ac - lv)), 5), "Levels": [round(x, 5) for x, _ in pts],
            "Values": [round(v, 5) for _, v in pts]}
