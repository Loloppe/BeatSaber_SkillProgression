"""Exploration 07: gaps, objectives and recommendations toward the next waypoint (D-009, D-022) + back-test.

For a player snapshot on their path (exploration 04): next waypoint -> push gap (difficulty D) and polish gap
(acc vs predicted A), normalised; map recommendations:
  push   = path maps in a difficulty window just above the player, not yet cleared, ranked by how many players at the
           next waypoint cleared them (popularity computed on TRAIN players only);
  polish = maps already cleared near the player's difficulty with the largest acc-vs-predicted shortfall.
Back-test on held-out players against what they did in the following quarter.
Outputs: data/explore/07_objectives/ (local only).
"""
import importlib
from collections import Counter, defaultdict

import numpy as np
import pandas as pd

from common import connect, load_leaderboards, load_players, load_scores, out_dir
from profiles import DESTINATIONS, apply_modifier_ratings, destination, path_plays

OUT = out_dir("07_objectives")
SNAPSHOTS = pd.date_range("2023-04-01", "2026-10-01", freq="QS")
K = 10
TEST_SHARE = 0.2
IMPROVED = 0.005  # acc gain that counts as "improved" a map
traj = importlib.import_module("02_trajectories")


def main():
    rng = np.random.default_rng(0)
    con = connect()
    lbs, players = load_leaderboards(con), load_players(con)
    mods = pd.read_sql_query("select * from ModifiersRating", con)
    scores = load_scores(con).merge(lbs, left_on="LeaderboardId", right_on="Id", suffixes=("", "_lb"))
    scores = apply_modifier_ratings(scores[scores.ModeName == "Standard"], mods)
    cur = pd.read_csv(f"{out_dir('03_thresholds')}/current_profiles.csv", dtype={"PlayerId": str})
    cur["dest"] = cur.apply(destination, axis=1)
    arrived = cur[(cur.Rank <= 10000) & cur.dest.isin(DESTINATIONS)].set_index("PlayerId").dest
    clears = traj.clear_attempts(set(arrived.index), lbs, mods, scores)
    clears["resid"] = clears.Accuracy - clears.PredictedAcc

    snaps = pd.read_csv(f"{out_dir('04_waypoints')}/path_snapshots.csv", dtype={"PlayerId": str}, parse_dates=["t"])
    wps = pd.read_csv(f"{out_dir('04_waypoints')}/waypoints.csv")
    snaps = snaps[snaps.path.isin(wps.path.unique())]
    # waypoint index from path pp (same cut points as exploration 04)
    snaps["wp"] = -1
    for path, w in wps.groupby("path"):
        m = snaps.path == path
        snaps.loc[m, "wp"] = np.searchsorted(w.path_pp_lo.to_numpy(), snaps.loc[m, "path_pp"].to_numpy(), side="right") - 1
    snaps = snaps.sort_values(["PlayerId", "t"])
    sd = snaps.groupby("path")[["D", "A"]].std()

    pids = snaps.PlayerId.unique()
    test = set(rng.choice(pids, int(len(pids) * TEST_SHARE), replace=False))

    # popularity of first clears per (path, waypoint), TRAIN players only
    pop = defaultdict(Counter)
    by_player = {pid: g.sort_values("Time") for pid, g in clears.groupby("PlayerId")}
    for (pid, path), g in snaps[~snaps.PlayerId.isin(test)].groupby(["PlayerId", "path"]):
        c = by_player.get(pid)
        if c is None:
            continue
        rel, _ = path_plays(c, path)
        first = rel.drop_duplicates("LeaderboardId")  # sorted by time -> first clear
        ts = g.t.tolist() + [pd.Timestamp.max]
        for (t0, wp), t1 in zip(zip(g.t, g.wp), ts[1:]):
            new = first[(first.Time >= t0) & (first.Time < t1)].LeaderboardId
            pop[(path, wp)].update(set(new))

    rows = []
    for (pid, path), g in snaps[snaps.PlayerId.isin(test)].groupby(["PlayerId", "path"]):
        c = by_player.get(pid)
        if c is None:
            continue
        rel, axis = path_plays(c, path)
        w = wps[wps.path == path].set_index("wp")
        g = g.reset_index(drop=True)
        for i in range(len(g) - 1):
            s, s1 = g.loc[i], g.loc[i + 1]
            k = int(s.wp)
            if k + 1 not in w.index:
                continue
            nxt = w.loc[k + 1]
            push_gap = max(0.0, nxt.D - s.D) / sd.loc[path, "D"]
            polish_gap = max(0.0, nxt.A - s.A) / sd.loc[path, "A"]
            before = rel[rel.Time < s.t]
            during = rel[(rel.Time >= s.t) & (rel.Time < s1.t)]
            if before.empty or during.empty:
                continue
            best_before = before.groupby("LeaderboardId").agg(acc=("Accuracy", "max"), resid=("resid", "max"),
                                                              difficulty=(axis, "first"))
            best_during = during.groupby("LeaderboardId").Accuracy.max()
            new_clears = set(best_during.index) - set(best_before.index)
            common = best_during.index.intersection(best_before.index)
            improved = set(common[(best_during[common] - best_before.loc[common, "acc"]) >= IMPROVED])
            actual = new_clears | improved
            if not actual:
                continue
            step = max(nxt.D - s.D, 0.25)
            # push candidates: path maps in the difficulty window, not yet cleared
            window = lbs_axis[path][(lbs_axis[path] >= s.D) & (lbs_axis[path] <= nxt.D + step)]
            cand = [lb for lb in window.index if lb not in best_before.index]
            push = sorted(cand, key=lambda lb: -pop[(path, k + 1)].get(lb, 0))[:K]
            # polish candidates: cleared maps near current difficulty with the biggest shortfall vs next waypoint
            near = best_before[(best_before.difficulty >= s.D - step) & (best_before.difficulty <= s.D + step)]
            polish = near.sort_values("resid").index[:K].tolist()
            polish_rand = list(rng.choice(near.index, min(K, len(near)), replace=False)) if len(near) else []
            wide = lbs_axis[path][(lbs_axis[path] >= s.D - step) & (lbs_axis[path] <= nxt.D + step)]
            push_wide = sorted([lb for lb in wide.index if lb not in best_before.index],
                               key=lambda lb: -pop[(path, k + 1)].get(lb, 0))[:K]
            rand = list(rng.choice(cand, min(K, len(cand)), replace=False)) if cand else []
            popular_global = [lb for lb, _ in pop[(path, k + 1)].most_common(K * 3) if lb not in best_before.index][:K]
            d_dD = (s1.D - s.D) / sd.loc[path, "D"]
            d_dA = (s1.A - s.A) / sd.loc[path, "A"]
            rows.append({
                "PlayerId": pid, "path": path, "wp": k, "push_gap": push_gap, "polish_gap": polish_gap,
                "predicted": "push" if push_gap > polish_gap else "polish",
                "actual_dir": "push" if d_dD > d_dA else "polish", "moved_up": s1.wp > s.wp,
                "hit_push": bool(set(push) & new_clears), "hit_polish": bool(set(polish) & improved),
                "hit_push_wide": bool(set(push_wide) & new_clears),
                "hit_polish_random": bool(set(polish_rand) & improved),
                "hit_combined": bool((set(push[:5]) | set(polish[:5])) & actual),
                "hit_random": bool(set(rand) & new_clears), "hit_popular": bool(set(popular_global) & new_clears),
                "prec_push": len(set(push) & new_clears) / K, "prec_random": len(set(rand) & new_clears) / K,
                "n_new": len(new_clears), "n_improved": len(improved), "n_window": len(cand),
            })
    res = pd.DataFrame(rows)
    res.to_csv(f"{OUT}/backtest.csv", index=False)
    rep = [f"# Exploration 07 — objectives & back-test\n\nTest players: {res.PlayerId.nunique():,} "
           f"({TEST_SHARE:.0%} held out), test snapshot pairs: {len(res):,}\n"]
    summary = res.groupby("path").agg(
        pairs=("PlayerId", "size"),
        dir_acc=("predicted", lambda x: (x == res.loc[x.index, "actual_dir"]).mean()),
        majority=("actual_dir", lambda x: x.value_counts(normalize=True).max()),
        hit_push=("hit_push", "mean"), hit_popular=("hit_popular", "mean"), hit_random=("hit_random", "mean"),
        hit_push_wide=("hit_push_wide", "mean"),
        hit_polish=("hit_polish", "mean"), hit_polish_random=("hit_polish_random", "mean"),
        hit_combined=("hit_combined", "mean"),
        prec_push=("prec_push", "mean"), prec_random=("prec_random", "mean"),
        new_clears=("n_new", "median"), window=("n_window", "median"))
    rep.append("\n## Per path (next quarter after each test snapshot)\n"
               "dir_acc = predicted biggest gap (push/polish) matches the player's actual dominant change; majority = "
               "always predicting the more common direction. hit_* = at least one of the 10 recommended maps was then "
               "newly cleared (push/random/popular) or improved by >= 0.5% (polish); combined = top-5 push + top-5 polish "
               "vs either.\n\n" + summary.round(3).to_markdown() + "\n")
    mv = res[res.moved_up]
    rep.append(f"\nOnly pairs where the player moved up a waypoint ({len(mv):,}): direction accuracy "
               f"{(mv.predicted == mv.actual_dir).mean():.3f}, majority {mv.actual_dir.value_counts(normalize=True).max():.3f}\n")
    open(f"{OUT}/report.md", "w", encoding="utf-8").write("".join(rep))
    print("".join(rep))


def path_axis_index(lbs, mods):
    """Per path: Series LeaderboardId -> axis rating (nomod ratings; recommendations are nomod maps for now)."""
    std = lbs[lbs.ModeName == "Standard"].set_index("Id")
    out = {}
    for path in DESTINATIONS:
        rel, axis = path_plays(std, path)
        out[path] = rel[axis]
    return out


if __name__ == "__main__":
    _con = connect()
    lbs_axis = path_axis_index(load_leaderboards(_con), None)
    main()
