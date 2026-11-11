"""Exploration 04: waypoints per path (D-009, D-022).

For players who arrived at a destination (rank <= ARRIVED_RANK, rules v6), rebuild their path state quarterly from
clears (attempts export) plus their current PBs: path pp (orders waypoints), difficulty reached D, accuracy at
difficulty A. Pool the snapshots per path and cut them into waypoints by path pp. For each waypoint report the typical
D / A / equivalent rank, how players moved to the next one (push = harder maps vs polish = better acc), and the maps
players at that waypoint newly cleared most often (candidate recommendations).
Outputs: data/explore/04_waypoints/ (gitignored; aggregates only would ever be published, D-007).
"""
import importlib

import numpy as np
import pandas as pd

from common import connect, load_leaderboards, load_players, load_scores, out_dir
from profiles import DESTINATIONS, apply_modifier_ratings, destination, path_plays, path_state

ARRIVED_RANK = 10000
SNAPSHOTS = pd.date_range("2023-04-01", "2026-10-01", freq="QS")
# Waypoints at equivalent-rank milestones (review round 3): finer near the top, where gaps are large, and readable
# ("players around rank 250"). A milestone with too few snapshots is merged into the next one.
RANK_MILESTONES = [40000, 30000, 20000, 15000, 10000, 7500, 5000, 3000, 2000, 1000, 500, 250, 100, 50, 25, 10]
MIN_SNAPS_PER_WAYPOINT = 40
OUT = out_dir("04_waypoints")
traj = importlib.import_module("02_trajectories")


def main():
    con = connect()
    lbs, players = load_leaderboards(con), load_players(con)
    mods = pd.read_sql_query("select * from ModifiersRating", con)
    scores = load_scores(con).merge(lbs, left_on="LeaderboardId", right_on="Id", suffixes=("", "_lb"))
    scores = apply_modifier_ratings(scores[scores.ModeName == "Standard"], mods)
    cur = pd.read_csv(f"{out_dir('03_thresholds')}/current_profiles.csv", dtype={"PlayerId": str})
    cur["dest"] = cur.apply(destination, axis=1)
    arrived = cur[(cur.Rank <= ARRIVED_RANK) & cur.dest.isin(DESTINATIONS)].set_index("PlayerId").dest
    clears = traj.clear_attempts(set(arrived.index), lbs, mods, scores)
    names = lbs.set_index("Id").Name
    pp_sorted = np.sort(players.Pp.to_numpy())[::-1]
    eq_rank = lambda pp: int(np.searchsorted(-pp_sorted, -pp) + 1)

    # --- snapshots of path state ---------------------------------------------------------------------------------
    rows = []
    for pid, g in clears.groupby("PlayerId"):
        path = arrived[pid]
        for t in SNAPSHOTS:
            sub = g[g.Time < t]
            if len(sub) < 20:
                continue
            best = sub.sort_values("Pp", ascending=False).drop_duplicates("LeaderboardId")
            st = path_state(best, path)
            if st:
                total = float((best.Pp.to_numpy() * 0.965 ** np.arange(len(best))).sum())
                rows.append({"PlayerId": pid, "path": path, "t": t, "eq_rank": eq_rank(total), **st})
    snaps = pd.DataFrame(rows)
    snaps.to_csv(f"{OUT}/path_snapshots.csv", index=False)
    rep = [f"# Exploration 04 — waypoints per path\n\nArrived players: {arrived.value_counts().to_dict()}\n"
           f"Path snapshots: {len(snaps):,} from {snaps.PlayerId.nunique():,} players\n"]

    # --- waypoints ------------------------------------------------------------------------------------------------
    all_wp = []
    for path in DESTINATIONS:
        s = snaps[snaps.path == path].copy()
        if len(s) < 2 * MIN_SNAPS_PER_WAYPOINT:
            rep.append(f"\n## {path}: only {len(s)} snapshots — too few for waypoints\n")
            continue
        # bucket b = snapshots whose equivalent rank is within milestone b (rank <= RANK_MILESTONES[b])
        bucket = np.searchsorted(-np.array(RANK_MILESTONES), -s.eq_rank.to_numpy(), side="right") - 1
        s["bucket"] = bucket
        s = s[s.bucket >= 0]
        groups, pending = [], []
        for b in range(len(RANK_MILESTONES)):
            pending.append(b)
            if (s.bucket.isin(pending)).sum() >= MIN_SNAPS_PER_WAYPOINT:
                groups.append(list(pending))
                pending = []
        if pending and groups:
            groups[-1] += pending  # too few at the very top: merge into the last waypoint
        rows = []
        for i, g in enumerate(groups):
            b = s[s.bucket.isin(g)]
            rows.append({"wp": i, "milestone_rank": RANK_MILESTONES[g[0]], "players": b.PlayerId.nunique(),
                         "path_pp_lo": float(b.path_pp.quantile(0.25)), "path_pp_hi": float(b.path_pp.quantile(0.75)),
                         "D": float(b.D.median()), "A": float(b.A.median()), "acc": float(b.acc.median()),
                         "eq_rank": float(b.eq_rank.median())})
        wp = pd.DataFrame(rows).set_index("wp")
        wp["path_pp_lo"] = wp.path_pp_lo.cummax()  # placement by path pp needs increasing lower bounds
        k = len(wp)
        # place every snapshot like Core does (highest waypoint whose path-pp lower bound is reached)
        s = snaps[snaps.path == path].copy()
        s["wp"] = np.maximum(np.searchsorted(wp.path_pp_lo.to_numpy(), s.path_pp.to_numpy(), side="right") - 1, 0)
        # how players moved up: consecutive snapshots of the same player that reach a higher waypoint
        s = s.sort_values(["PlayerId", "t"])
        nxt = s.groupby("PlayerId").shift(-1)
        up = s[nxt.wp > s.wp].assign(dD=nxt.D - s.D, dA=nxt.A - s.A)
        sdD, sdA = s.D.std(), s.A.std()
        up["push"] = (up.dD / sdD) > (up.dA / sdA)
        moves = up.groupby("wp").agg(moves=("push", "size"), push_share=("push", "mean"),
                                     dD=("dD", "median"), dA=("dA", "median"))
        wp = wp.join(moves)
        rep.append(f"\n## {path} — {k} waypoints (ordered by path pp; D = typical difficulty on the path axis, "
                   f"A = acc vs predicted at that difficulty)\n\n" + wp.round(3).to_markdown() + "\n")
        all_wp.append(wp.assign(path=path))

        # maps newly cleared by players while at each waypoint (candidate recommendations)
        recs = []
        snap_t = s[["PlayerId", "t", "wp"]].rename(columns={"t": "t0"})
        snap_t["t1"] = snap_t.groupby("PlayerId").t0.shift(-1)
        g = clears[clears.PlayerId.isin(s.PlayerId.unique())]
        rel, axis = path_plays(g, path)
        first = rel.sort_values("Time").drop_duplicates(["PlayerId", "LeaderboardId"])
        m = first.merge(snap_t.dropna(subset=["t1"]), on="PlayerId")
        m = m[(m.Time >= m.t0) & (m.Time < m.t1)]
        for w, gg in m.groupby("wp"):
            top = gg.groupby("LeaderboardId").agg(players=("PlayerId", "nunique"), acc=("Accuracy", "median"),
                                                  difficulty=(axis, "median")).sort_values("players", ascending=False).head(5)
            for lb, r in top.iterrows():
                recs.append({"wp": w, "map": names.get(lb, lb), "players": r.players, axis: round(r.difficulty, 2),
                             "median acc": round(r.acc, 4)})
        if recs:
            rep.append(f"\nMost common new clears per waypoint (top 5):\n\n"
                       + pd.DataFrame(recs).to_markdown(index=False) + "\n")
    pd.concat(all_wp).to_csv(f"{OUT}/waypoints.csv")
    open(f"{OUT}/report.md", "w", encoding="utf-8").write("".join(rep))
    print("".join(rep))


if __name__ == "__main__":
    main()
