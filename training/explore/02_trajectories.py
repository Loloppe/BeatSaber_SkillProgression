"""Exploration 02: when does a player's style start to predict the destination they end up in?

1. Label current destinations with the D-018 rules (effective ratings) and show how the rules split rank bands.
2. Rebuild historical profiles of players who "arrived" (current rank <= ARRIVED_RANK) from the attempts export
   (clears only), quarterly. Attempt pp is estimated from the population's PB (acc -> pp) curve of the same
   leaderboard and speed modifier.
3. Divergence: for each historical level (expressed as the equivalent current rank), how well do the
   level-independent style features at that time predict the final destination (cross-validated by player)?
Outputs: data/explore/02_trajectories/ (gitignored). Raw attempts never leave data/ (D-007).
"""
import hashlib
import os

import numpy as np
import pandas as pd
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import balanced_accuracy_score
from sklearn.model_selection import GroupKFold, cross_val_predict
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt

from common import connect, load_attempts, load_leaderboards, load_players, load_scores, out_dir
from profiles import DESTINATIONS, RATING_COLS, TOP_N, apply_modifier_ratings, destination, profile

MIN_SCORES = 30
ARRIVED_RANK = 10000
SNAPSHOTS = pd.date_range("2023-04-01", "2026-10-01", freq="QS")
MIN_PLAYS_AT_SNAPSHOT = 20
OUT = out_dir("02_trajectories")
DESTS = list(DESTINATIONS)


def bl_weighted(pp_desc: np.ndarray) -> float:
    return float((pp_desc * 0.965 ** np.arange(len(pp_desc))).sum())


def current_profiles(scores, players):
    scores = scores.sort_values(["PlayerId", "Pp"], ascending=[True, False])
    counts = scores.groupby("PlayerId").size()
    scores = scores[scores.PlayerId.isin(counts[counts >= MIN_SCORES].index)]
    rows = []
    for pid, g in scores.groupby("PlayerId", sort=False):
        p = profile(g.head(TOP_N), float(np.quantile(g.PassRating, 0.95)))
        rows.append({"PlayerId": pid, **p, "destination": destination(p)})
    return pd.DataFrame(rows).merge(players, left_on="PlayerId", right_on="Id").sort_values("Rank")


def pp_curves(scores):
    """(LeaderboardId, SpeedMod) -> (acc sorted, pp) from the population's PBs."""
    curves = {}
    for key, g in scores.groupby(["LeaderboardId", "SpeedMod"]):
        if len(g) >= 5:
            g = g.sort_values("Accuracy")
            curves[key] = (g.Accuracy.to_numpy(), g.Pp.to_numpy())
    return curves


def estimate_pp(att, curves):
    att["Pp"] = np.nan
    for (lb, sm), idx in att.groupby(["LeaderboardId", "SpeedMod"]).groups.items():
        c = curves.get((lb, sm)) or (curves.get((lb, "")) if sm == "" else None)
        if c is not None:
            att.loc[idx, "Pp"] = np.interp(att.loc[idx, "Accuracy"].to_numpy(), c[0], c[1])
    return att.dropna(subset=["Pp"])


def clear_attempts(player_ids: set, lbs, mods, scores) -> pd.DataFrame:
    """Clears of the given players with effective ratings and estimated pp. Cached in data/explore/cache/ (local only)."""
    key = hashlib.sha1(",".join(sorted(player_ids)).encode()).hexdigest()[:10]
    cache = os.path.join(out_dir("cache"), f"clears_{len(player_ids)}_{key}.pkl")
    if os.path.exists(cache):
        return pd.read_pickle(cache)
    att, meta = load_attempts(["Player", "Leaderboard", "Type", "Modifiers", "Accuracy", "Timeset"])
    att = att[(att.Type == 1) & (att.Accuracy > 0)]
    pl_ids, lb_ids, mod_ids = np.array(meta["Players"]), np.array(meta["Leaderboards"]), np.array(meta["Modifiers"])
    att["PlayerId"] = pl_ids[att.Player.to_numpy()]
    att = att[att.PlayerId.isin(player_ids)]
    att["LeaderboardId"] = lb_ids[att.Leaderboard.to_numpy()]
    att["Modifiers"] = mod_ids[att.Modifiers.to_numpy()]
    att = att.drop(columns=["Player", "Leaderboard", "Type"])
    lb_cols = ["Id", "ModeName", *RATING_COLS, "MapType", "Duration", "PeakEbpm", "ModifiersRatingId"]
    att = att.merge(lbs[lb_cols], left_on="LeaderboardId", right_on="Id")
    att = att[att.ModeName == "Standard"]
    att = apply_modifier_ratings(att, mods)
    att = estimate_pp(att, pp_curves(scores))
    att["Time"] = pd.to_datetime(att.Timeset, unit="s")
    keep = ["PlayerId", "LeaderboardId", "Modifiers", "SpeedMod", "Accuracy", "Pp", "Time", *RATING_COLS,
            "MapType", "Duration", "PeakEbpm"]
    att = att[keep].reset_index(drop=True)
    att.to_pickle(cache)
    return att


def main():
    con = connect()
    lbs, players = load_leaderboards(con), load_players(con)
    mods = pd.read_sql_query("select * from ModifiersRating", con)
    scores = load_scores(con).merge(lbs, left_on="LeaderboardId", right_on="Id", suffixes=("", "_lb"))
    scores = apply_modifier_ratings(scores[scores.ModeName == "Standard"], mods)
    rep = ["# Exploration 02 — trajectories & divergence\n"]

    # --- 1. current destinations ------------------------------------------------------------------------
    cur = current_profiles(scores, players)
    cur.to_csv(f"{OUT}/current_profiles.csv", index=False)
    bands = [(1, 500), (501, 2000), (2001, 5000), (5001, 10000), (10001, 20000), (20001, 50000), (50001, 10**7)]
    rep.append("\n## 1. Rule-based destinations (D-018, first-guess thresholds) by current rank band\n\n")
    tab = []
    for lo, hi in bands:
        b = cur[(cur.Rank >= lo) & (cur.Rank <= hi)]
        tab.append({"ranks": f"{lo}-{hi}", "players": len(b),
                    **b.destination.value_counts(normalize=True).reindex(DESTS).fillna(0).round(3).to_dict()})
    rep.append(pd.DataFrame(tab).to_markdown(index=False) + "\n")
    arrived = cur[cur.Rank <= ARRIVED_RANK].set_index("PlayerId").destination
    rep.append(f"\nArrived players (rank <= {ARRIVED_RANK}): {len(arrived):,} — "
               f"{arrived.value_counts().to_dict()}\n")

    # --- 2. historical profiles from attempts -----------------------------------------------------------
    att = clear_attempts(set(arrived.index), lbs, mods, scores)
    rep.append(f"\nClear attempts of arrived players with estimated pp: {len(att):,}\n")

    pp_sorted = np.sort(players.Pp.to_numpy())[::-1]

    def equivalent_rank(pp):  # where this pp would sit in today's ranking
        return int(np.searchsorted(-pp_sorted, -pp) + 1)

    snaps = []
    for pid, g in att.groupby("PlayerId"):
        g = g.sort_values("Time")
        for t in SNAPSHOTS:
            sub = g[g.Time < t]
            if sub.LeaderboardId.nunique() < MIN_PLAYS_AT_SNAPSHOT:
                continue
            best = sub.sort_values("Pp", ascending=False).drop_duplicates("LeaderboardId")
            ceiling = float(np.quantile(best.PassRating, 0.95))
            p = profile(best.head(TOP_N), ceiling)
            est_pp = bl_weighted(best.Pp.to_numpy())
            snaps.append({"PlayerId": pid, "t": t, "est_pp": est_pp, "eq_rank": equivalent_rank(est_pp),
                          "final": arrived[pid], **p})
    snaps = pd.DataFrame(snaps)
    snaps.to_csv(f"{OUT}/snapshots.csv", index=False)
    rep.append(f"Snapshots: {len(snaps):,} from {snaps.PlayerId.nunique():,} players\n")

    # --- 3. divergence ------------------------------------------------------------------------------------
    style = [c for c in snaps.columns if c.startswith("s_")]
    eq_bands = [(1, 2000), (2001, 5000), (5001, 10000), (10001, 20000), (20001, 40000), (40001, 80000), (80001, 10**7)]
    rep.append("\n## 3. Does style at an earlier level predict the final destination?\n"
               "Multinomial logistic regression on level-independent style features, 5-fold CV grouped by player, "
               "trained per equivalent-rank band (level at snapshot time). Balanced accuracy; chance = 1/#classes.\n\n")
    rows = []
    for lo, hi in eq_bands:
        b = snaps[(snaps.eq_rank >= lo) & (snaps.eq_rank <= hi)]
        classes = b.final.value_counts()
        b = b[b.final.isin(classes[classes >= 30].index)]
        if b.final.nunique() < 2 or b.PlayerId.nunique() < 100:
            continue
        model = make_pipeline(StandardScaler(), LogisticRegression(max_iter=2000, class_weight="balanced"))
        pred = cross_val_predict(model, b[style], b.final, groups=b.PlayerId, cv=GroupKFold(5))
        recall = {d: round(float((pred[b.final == d] == d).mean()), 2) for d in DESTS if (b.final == d).any()}
        rows.append({"equivalent rank": f"{lo}-{hi}", "snapshots": len(b), "players": b.PlayerId.nunique(),
                     "classes": b.final.nunique(), "balanced acc": round(balanced_accuracy_score(b.final, pred), 3),
                     "chance": round(1 / b.final.nunique(), 3), **{f"recall {k}": v for k, v in recall.items()}})
    div = pd.DataFrame(rows)
    rep.append(div.to_markdown(index=False) + "\n")

    fig, ax = plt.subplots(figsize=(9, 4))
    ax.plot(div["equivalent rank"], div["balanced acc"], marker="o", label="balanced accuracy")
    ax.plot(div["equivalent rank"], div["chance"], ls="--", label="chance")
    ax.set_xlabel("level at snapshot (equivalent current rank)"); ax.legend()
    plt.setp(ax.get_xticklabels(), rotation=30); fig.tight_layout(); fig.savefig(f"{OUT}/divergence.png", dpi=110)

    open(f"{OUT}/report.md", "w", encoding="utf-8").write("".join(rep))
    print("".join(rep))


if __name__ == "__main__":
    main()
