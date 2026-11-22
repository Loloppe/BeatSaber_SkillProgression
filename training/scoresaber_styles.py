"""The five styles measured on ScoreSaber (D-062), built cross-sectionally from sampled ScoreSaber players.

Inputs: fetch_scoresaber.py (14_scoresaber: ranked leaderboards with stars and modifier values, pp curve, pp at each
global rank milestone, top scores of ~80 players sampled around each milestone). Each sampled player sits at the
milestone nearest their global rank (log scale; samples around neighbouring milestones overlap near the top). Their
scores on model maps (no-modifier BeatLeader ratings, SS pp as ScoreSaber computed it) give, for every style where
they have >= 5 plays, their path state (profiles.path_state: SS path pp, D, A, acc). Unlike exploration 04, players
are not filtered by destination: without history the sample is too thin for that (only ~55% arrive anywhere; Accuracy
and Extreme had a handful), so a waypoint is "players at this SS rank who play the style". Milestones with fewer than
MIN_PLAYERS_PER_WAYPOINT players merge upward. Push prior and gating are
the BeatLeader style's (same style dynamics, no SS history); spreads come from the SS players. Map popularity
"SS <style>|wp" is copied from the BeatLeader waypoint with the nearest equivalent rank (D-062 d).
Aggregates only (D-007): waypoint values are quantiles / medians over >= MIN_PLAYERS_PER_WAYPOINT players.
"""
import json
import math
import os
from collections import Counter

import numpy as np
import pandas as pd

from common import out_dir
from profiles import DESTINATIONS, path_state

PREFIX = "SS "  # Core ScoreSaberStyles.Prefix
DIFFICULTIES = {1: "Easy", 3: "Normal", 5: "Hard", 7: "Expert", 9: "ExpertPlus"}
MIN_PLAYERS_PER_WAYPOINT = 20


def load():
    d = out_dir("14_scoresaber")
    if not os.path.exists(os.path.join(d, "rank_pp.json")):
        print("no ScoreSaber data (run fetch_scoresaber.py) - no ScoreSaber styles")
        return None
    lbs = json.load(open(os.path.join(d, "leaderboards.json"), encoding="utf-8"))
    curve = json.load(open(os.path.join(d, "curve.json"), encoding="utf-8"))["curve"]
    values = Counter(json.dumps(sorted(x["ModifierValues"])) for x in lbs if not x["PositiveModifiers"])
    if len(values) != 1 or any(x["PositiveModifiers"] for x in lbs):
        print(f"  ScoreSaber modifier values differ between leaderboards: {values.most_common(3)} - using the most common")
    pts = sorted((float(a), float(m)) for a, m in curve)
    section = {"Curve": {"Acc": [round(a, 6) for a, _ in pts], "Pp": [round(m, 6) for _, m in pts]},
               "Factor": 42.117, "Decay": 0.965,
               "ModifierValues": {k: v for k, v in json.loads(values.most_common(1)[0][0])}}
    stars = {(x["Hash"], DIFFICULTIES.get(x["Difficulty"], "")): x["Stars"] for x in lbs if x["GameMode"] == "SoloStandard"}
    return section, stars, json.load(open(os.path.join(d, "rank_pp.json"), encoding="utf-8"))


def annotate(maps, stars):
    n = 0
    for m in maps:
        s = stars.get((m["Hash"], m["Difficulty"]))
        if m["RankedScoreSaber"] and s:
            m["ScoreSaberStars"] = float(s)
            n += 1
    print(f"ScoreSaber styles: {n:,} model maps with SS stars")


def player_plays(pid, maps, index):
    path = os.path.join(out_dir(os.path.join("14_scoresaber", "players")), f"{pid}.json")
    if not os.path.exists(path):
        return None
    rows = []
    for h, diff, mode, acc, mods, pp in json.load(open(path, encoding="utf-8")):
        i = index.get((h, DIFFICULTIES.get(diff, "")))
        if mode != "SoloStandard" or i is None:
            continue
        r, m = maps[i]["Ratings"][""], maps[i]
        rows.append({"map": i, "Accuracy": acc, "Pp": pp, "Stars": r["Stars"], "PassRating": r["Pass"],
                     "AccRating": r["Acc"], "TechRating": r["Tech"], "PredictedAcc": r["PredictedAcc"],
                     "MapType": m.get("MapType", ""), "Duration": m.get("Duration", 0), "PeakEbpm": m.get("PeakEbpm", 0),
                     "SpeedMod": ""})
    return pd.DataFrame(rows).sort_values("Pp", ascending=False) if rows else None


def states(maps, rank_pp):
    index = {(m["Hash"], m["Difficulty"]): i for i, m in enumerate(maps) if m.get("ScoreSaberStars")}
    milestones = [int(m) for m in rank_pp["Samples"]]
    out = []
    for pid, rank in rank_pp["Ranks"].items():
        best = player_plays(pid, maps, index)
        if best is None:
            continue
        milestone = min(milestones, key=lambda m: abs(math.log(m) - math.log(rank)))
        for style in DESTINATIONS:
            st = path_state(best, style)
            if st:
                out.append({"player": pid, "style": style, "milestone": milestone, **st})
    st = pd.DataFrame(out)
    print(f"ScoreSaber styles: {st.player.nunique() if len(st) else 0} sampled players with a style state "
          f"({st.style.value_counts().to_dict() if len(st) else {}})")
    return st


def waypoints(st, bl_paths):
    bl = {p["Destination"]: p for p in bl_paths}
    paths, ranks = [], {}
    for style in DESTINATIONS:
        t = st[st.style == style]
        if len(t) < 2 * MIN_PLAYERS_PER_WAYPOINT or style not in bl:
            print(f"  {PREFIX}{style}: only {len(t)} players - no waypoints")
            continue
        milestones = sorted(t.milestone.unique(), reverse=True)  # easiest (largest rank) first
        groups, pending = [], []
        for ms in milestones:
            pending.append(ms)
            if t.milestone.isin(pending).sum() >= MIN_PLAYERS_PER_WAYPOINT:
                groups.append(list(pending))
                pending = []
        if pending and groups:
            groups[-1] += pending
        wps, low = [], -np.inf
        for i, g in enumerate(groups):
            b = t[t.milestone.isin(g)]
            low = max(low, float(b.path_pp.quantile(0.25)))
            wps.append({"Index": i, "PathPpLow": round(low, 2), "PathPpHigh": round(float(b.path_pp.quantile(0.75)), 2),
                        "Difficulty": round(float(b.D.median()), 4), "AccuracyResidual": round(float(b.A.median()), 5),
                        "Accuracy": round(float(b.acc.median()), 5), "EquivalentRank": int(g[0])})
            print(f"  {PREFIX + style:14} wp {i}  rank ~{g[0]:6,}  players {len(b):3}  path pp {b.path_pp.quantile(0.25):7.0f}-"
                  f"{b.path_pp.quantile(0.75):7.0f}  D {b.D.median():5.2f}  acc {b.acc.median():.4f}")
        ranks[style] = [w["EquivalentRank"] for w in wps]
        # Ungated BL styles (Accuracy, Midspeed) are gated on accuracy here (user, D-062): the mixed sample's top
        # milestones have low path pp, so path pp alone placed a rank-1,000 player at the rank-50 Accuracy milestone.
        paths.append({"Destination": PREFIX + style, "PushPrior": bl[style]["PushPrior"],
                      "DifficultyGated": bl[style]["DifficultyGated"], "AccuracyGated": not bl[style]["DifficultyGated"], "DifficultySpread": round(float(t.D.std()), 4),
                      "AccuracySpread": round(float(t.A.std()), 5), "Waypoints": wps})
    return paths, ranks


def popularity(maps, bl_paths, ranks):
    """MapEntry.PeerPopularity["SS <style>|i"] = the BeatLeader style's popularity at the nearest-rank waypoint."""
    bl = {p["Destination"]: [w["EquivalentRank"] for w in p["Waypoints"]] for p in bl_paths}
    for style, ss_ranks in ranks.items():
        nearest = [min(range(len(bl[style])), key=lambda j: abs(math.log(bl[style][j]) - math.log(r))) for r in ss_ranks]
        for m in maps:
            if not m.get("ScoreSaberStars") or not m.get("PeerPopularity"):
                continue
            pop = m["PeerPopularity"]
            for i, j in enumerate(nearest):
                n = pop.get(f"{style}|{j}")
                if n:
                    pop[f"{PREFIX}{style}|{i}"] = n


def build(maps, bl_paths):
    """Annotates ScoreSaber stars and popularity in place; returns (SS style PathModels, ScoreSaberModel section)."""
    loaded = load()
    if loaded is None:
        return [], None
    section, stars, rank_pp = loaded
    annotate(maps, stars)
    st = states(maps, rank_pp)
    if st.empty:
        return [], section
    paths, ranks = waypoints(st, bl_paths)
    popularity(maps, bl_paths, ranks)
    return paths, section
