"""Exploration 03: calibrate destination thresholds (D-018, D-019) for profiles.destination().

Accuracy is relative to BL predicted acc (s_acc_resid); main star band decides the family; tech share is measured
within the band. Checks: the user's example players must land correctly; population per destination by rank band;
reference acc clusters from exploration 01 (consistency only, not ground truth — user sign-off is the validation).
"""
import importlib
import itertools

import numpy as np
import pandas as pd

from common import anchors, connect, load_leaderboards, load_players, load_scores, out_dir
from profiles import apply_modifier_ratings, destination

OUT = out_dir("03_thresholds")
SRC01 = out_dir("01_archetypes")
USER_EXAMPLES = anchors()  # user-provided anchors, local only (data/anchors.json)
ORDER = ["low-star acc", "mid-star acc", "high tech", "speed", "extreme", "unspecialised"]


def f1(pred, ref):
    tp = (pred & ref).sum()
    return 0.0 if tp == 0 else 2 * tp / (pred.sum() + ref.sum())


def main():
    con = connect()
    lbs, players = load_leaderboards(con), load_players(con)
    mods = pd.read_sql_query("select * from ModifiersRating", con)
    scores = load_scores(con).merge(lbs, left_on="LeaderboardId", right_on="Id", suffixes=("", "_lb"))
    scores = apply_modifier_ratings(scores[scores.ModeName == "Standard"], mods)
    cur = importlib.import_module("02_trajectories").current_profiles(scores, players)
    cur["PlayerId"] = cur.PlayerId.astype(str)
    cur.to_csv(f"{OUT}/current_profiles.csv", index=False)

    top = pd.read_csv(f"{SRC01}/top_players_clusters.csv", dtype={"PlayerId": str})
    means = top.groupby("c5")[["stars", "acc"]].mean()
    low_c = means.stars.idxmin()
    mid_c = means.drop(low_c).acc.idxmax()
    ref = cur.merge(top[["PlayerId", "c5"]], on="PlayerId")
    ref_low, ref_mid = (ref.c5 == low_c).to_numpy(), (ref.c5 == mid_c).to_numpy()
    ex = cur[cur.PlayerId.isin(USER_EXAMPLES)]

    rows = []
    for r_mid, pass_mid, gap in itertools.product([-0.02, -0.015, -0.01, -0.005, 0.0], [6.0, 7.0, 8.0, 9.0, 10.0, 11.0],
                                                  [0.0, 1.0, 2.0]):
        kw = dict(r_low=-0.01, r_mid=r_mid, pass_mid=pass_mid, gap_mid=gap)
        d = ref.apply(lambda p: destination(p, **kw), axis=1)
        dex = ex.apply(lambda p: destination(p, **kw), axis=1)
        rows.append({**kw, "examples ok": int((dex.to_numpy() == ex.PlayerId.map(USER_EXAMPLES).to_numpy()).sum()),
                     "F1 low": f1((d == "low-star acc").to_numpy(), ref_low),
                     "F1 mid": f1(d.str.startswith("mid-star").to_numpy(), ref_mid)})
    grid = pd.DataFrame(rows)
    grid["F1 mean"] = (grid["F1 low"] + grid["F1 mid"]) / 2
    grid = grid.sort_values(["examples ok", "F1 mean"], ascending=False)
    grid.to_csv(f"{OUT}/grid.csv", index=False)
    rep = ["# Exploration 03 — threshold calibration (v3 rules)\n",
           f"\nUser examples found in profiles: {len(ex)}/6. Reference acc clusters (01): low c5={low_c} "
           f"(n={ref_low.sum()}), mid c5={mid_c} (n={ref_mid.sum()}).\n",
           "\n## Best thresholds\n\n" + grid.head(8).round(3).to_markdown(index=False) + "\n"]
    best = grid.iloc[0]
    kw = dict(r_low=best.r_low, r_mid=best.r_mid, pass_mid=best.pass_mid, gap_mid=best.gap_mid)

    cur["dest"] = cur.apply(lambda p: destination(p, **kw), axis=1)
    cur.to_csv(f"{OUT}/destinations.csv", index=False)
    rep.append("\nUser examples: " + ", ".join(f"{r.PlayerId[-4:]}→{r.dest}" for r in cur[cur.PlayerId.isin(USER_EXAMPLES)].itertuples()) + "\n")
    tab = []
    for lo, hi in [(1, 500), (501, 2000), (2001, 5000), (5001, 10000), (10001, 20000), (20001, 50000)]:
        b = cur[(cur.Rank >= lo) & (cur.Rank <= hi)]
        tab.append({"ranks": f"{lo}-{hi}", "players": len(b),
                    **b.dest.value_counts(normalize=True).reindex(ORDER).fillna(0).round(3).to_dict()})
    rep.append(f"\n## Destinations with {kw}\n\n" + pd.DataFrame(tab).to_markdown(index=False) + "\n")
    summary = cur.groupby("dest").agg(players=("PlayerId", "size"), median_rank=("Rank", "median"),
                                      stars=("stars", "median"), acc=("acc", "median"),
                                      acc_vs_pred=("s_acc_resid", "median"), pass_=("pass", "median"),
                                      pass_ceiling=("pass_ceiling", "median"),
                                      tech5plus=("tech_share_5plus", "median"),
                                      tech9plus=("tech_share_9plus", "median"),
                                      sub95=("s_sub95", "median")).reindex(ORDER)
    rep.append("\nMedians per destination:\n\n" + summary.round(3).to_markdown() + "\n")
    examples = (cur[cur.Rank <= 5000].sort_values("Rank").groupby("dest").head(3)
                [["dest", "PlayerId", "Rank", "stars", "acc", "s_acc_resid", "pass", "pass_ceiling",
                  "tech_share_5plus", "tech_share_9plus"]])
    rep.append("\nExamples (best-ranked 3 per destination, for user review):\n\n"
               + examples.sort_values(["dest", "Rank"]).round(3).to_markdown(index=False) + "\n")
    open(f"{OUT}/report.md", "w", encoding="utf-8").write("".join(rep))
    print("".join(rep))


if __name__ == "__main__":
    main()
