"""Exploration 05: overlap of the ranked pools (BeatLeader, ScoreSaber, AccSaber) by (hash, difficulty, mode).

Downloads the public ScoreSaber v2 ranked list and the AccSaber ranked difficulties once (cached in data/explore/05_pools/),
BeatLeader ranked = population DB leaderboards. Answers: can a player progress on one pool only, per destination?
"""
import json
import os
import time
import urllib.request

import pandas as pd

from common import connect, load_leaderboards, out_dir
from profiles import THRESHOLDS, map_tech_led

OUT = out_dir("05_pools")
UA = {"User-Agent": "SkillProgression-research/0.1 (offline analysis)"}
DIFF_SS = {1: "Easy", 3: "Normal", 5: "Hard", 7: "Expert", 9: "ExpertPlus"}
DIFF_AS = {"EASY": "Easy", "NORMAL": "Normal", "HARD": "Hard", "EXPERT": "Expert", "EXPERT_PLUS": "ExpertPlus"}


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60) as r:
        return json.load(r)


def scoresaber():
    path = f"{OUT}/scoresaber_ranked.json"
    if not os.path.exists(path):
        items, page = [], 1
        while True:
            d = get(f"https://scoresaber.com/api/v2/leaderboards?status=RANKED&limit=100&page={page}")
            items += d["data"]
            if page >= d["metadata"]["totalPages"]:
                break
            page += 1
            time.sleep(0.3)
        json.dump(items, open(path, "w"))
    rows = []
    for it in json.load(open(path)):
        mode = it["difficulty"]["gameMode"]
        rows.append({"hash": it["map"]["hash"].upper(), "diff": DIFF_SS[it["difficulty"]["difficulty"]],
                     "mode": mode[4:] if mode.startswith("Solo") else mode, "ss_stars": it["realm"]["stars"]})
    return pd.DataFrame(rows)


def accsaber():
    path = f"{OUT}/accsaber_ranked.json"
    if not os.path.exists(path):
        json.dump(get("https://api.accsaber.com/v1/maps/difficulties/all"), open(path, "w"))
    d = json.load(open(path))
    d = d if isinstance(d, list) else d.get("content", d.get("data", []))
    return pd.DataFrame([{"hash": x["songHash"].upper(), "diff": DIFF_AS[x["difficulty"]], "mode": "Standard",
                          "as_category": x["categoryCode"], "as_complexity": x["complexity"]} for x in d])


def main():
    bl = load_leaderboards(connect())
    bl = bl.assign(hash=bl.Hash.str.upper(), diff=bl.DifficultyName, mode=bl.ModeName)
    ss, acs = scoresaber(), accsaber()
    key = ["hash", "diff", "mode"]
    m = (bl[key + ["Id", "Stars", "PassRating", "TechRating", "PredictedAcc", "MapType"]]
         .merge(ss, on=key, how="outer").merge(acs, on=key, how="outer"))
    m["BL"], m["SS"], m["AS"] = m.Id.notna(), m.ss_stars.notna(), m.as_category.notna()
    m.to_csv(f"{OUT}/pools.csv", index=False)
    rep = [f"# Exploration 05 — ranked pool overlap\n\nBL ranked (population DB): {m.BL.sum():,}, "
           f"ScoreSaber ranked: {m.SS.sum():,}, AccSaber ranked: {m.AS.sum():,}\n\n"]
    combos = m.groupby(["BL", "SS", "AS"]).size().rename("difficulties").reset_index()
    rep.append(combos.to_markdown(index=False) + "\n")
    rep.append(f"\nShare of SS-ranked also BL-ranked: {(m.SS & m.BL).sum() / m.SS.sum():.1%}; "
               f"share of AccSaber also BL-ranked: {(m.AS & m.BL).sum() / m.AS.sum():.1%}; "
               f"also SS-ranked: {(m.AS & m.SS).sum() / m.AS.sum():.1%}\n")
    # per-destination coverage of each pool, using BL ratings where the map is BL-ranked
    r = m[m.BL].copy()
    t = THRESHOLDS
    tl = map_tech_led(r.PassRating, r.TechRating)
    r["zone"] = "Speed/Extreme (pass > 8)"
    r.loc[~tl & (r.PassRating <= t["mid_pass"]) & (r.PassRating > t["low_pass"]), "zone"] = "Midspeed (pass 3-8)"
    r.loc[r.PassRating <= t["low_pass"], "zone"] = "Accuracy (pass <= 3)"
    r.loc[tl, "zone"] = "High Tech (tech-led)"
    cov = r.groupby("zone").agg(BL=("BL", "sum"), also_SS=("SS", "sum"), also_AccSaber=("AS", "sum"))
    rep.append("\nBL-ranked maps by destination zone, and how many are also in the other pools:\n\n"
               + cov.astype(int).to_markdown() + "\n")
    ss_only = m[m.SS & ~m.BL]
    rep.append(f"\nSS-ranked but not BL-ranked: {len(ss_only):,} (SS stars median {ss_only.ss_stars.median():.2f}, "
               f"p90 {ss_only.ss_stars.quantile(.9):.2f}) — would need offline ratings (RatingAPI/Analyzer).\n")
    rep.append("\nAccSaber category vs BL zone (maps in both):\n\n"
               + pd.crosstab(r[r.AS].as_category, r[r.AS].zone).to_markdown() + "\n")
    open(f"{OUT}/report.md", "w", encoding="utf-8").write("".join(rep))
    print("".join(rep))


if __name__ == "__main__":
    main()
