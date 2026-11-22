"""Incremental model update (user request 2026-11-22): today's ranked / curated maps without retraining.

Loads data/model/model.json and refreshes it from the platforms' current lists:
- BeatLeader `/leaderboards?type=ranked` (ratings per speed modifier, NJS, NPS, ranked date in one list, 100 per page),
  ScoreSaber ranked leaderboards (stars), AccSaber ranked difficulties (category, complexity), BeatSaver curated list.
- Existing maps: pool flags, ScoreSaber stars, AccSaber category / complexity, curated flag / BeatSaver score. Their
  BeatLeader ratings, pp curves and peer data stay as built (consistent with the fits; a full build refreshes them).
  Maps ranked nowhere any more and not curated are dropped.
- Maps that become ranked (new, or curated-only before): BeatLeader ratings (from the ranked list, else
  `/leaderboards/hash`, cached), acc -> pp curves from the closest existing curves (build_model.knn_curves), peer curves
  from their BeatLeader scores (`/leaderboard/{id}/scoregraph`, cached) against the levels the last full build fitted
  (peer_targets.map_curve, levels.csv.gz), and popularity (user choice): per style, the training-set players
  (exploration 04) at each milestone, by their latest snapshot, who set a score on the map after it was ranked — the
  map is new, so a score is a new clear at about their current milestone. ScoreSaber-style keys are copied from the
  nearest-rank BeatLeader milestone as in the full build. AccSaber track popularity is left empty (only a tiebreak
  after AP gain there, D-061).
- New curated maps ranked nowhere: ratings only (Explore's Curated pool, D-045).
Unchanged until the next full build: milestones, fitted player levels, style rules, AccSaber / ScoreSaber ladders.
Run from training/:  .venv/Scripts/python update_model.py   (needs a full build_model.py run after D-064 first:
levels.csv.gz / band_edges.json). The previous model is kept as data/model/model.<date>.json.gz.
"""
import gzip
import json
import os
import shutil
import sys
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone

import numpy as np
import pandas as pd

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "explore"))
from common import out_dir  # noqa: E402
import accsaber_tracks  # noqa: E402
import build_model  # noqa: E402
import peer_targets  # noqa: E402
import scoresaber_styles  # noqa: E402
from fetch_curated import curated_list  # noqa: E402
from fetch_peer_scores import fetch as fetch_scores  # noqa: E402
from fetch_pool_ratings import USER_AGENT, WORKERS, fetch as fetch_song, ratings  # noqa: E402
from fetch_scoresaber import leaderboards as fetch_ss_leaderboards  # noqa: E402
from profiles import DESTINATIONS, path_plays  # noqa: E402

MODEL_DIR = build_model.OUT_DIR
BL_RANKED = "https://api.beatleader.com/leaderboards?type=ranked&page={}&count=100"
ACCSABER = "https://api.accsaber.com/v1/maps/difficulties/all"
TYPE_BITS = build_model.TYPE_BITS


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read().decode("utf-8"))


def all_ratings(d):
    """{"": …, "SS"/"FS"/"SF": …} from a BeatLeader difficulty (fetch_pool_ratings.ratings); None when unrated."""
    base = ratings(d)
    if base is None:
        return None
    r = {"": base}
    mr = d.get("modifiersRating") or {}
    for code in ("SS", "FS", "SF"):
        if (m := ratings(mr, code.lower())) is not None:
            r[code] = m
    return r


def entry(song, d, r):
    return {"Hash": song["hash"].upper(), "Characteristic": "Standard", "Difficulty": d["difficultyName"],
            "Name": song.get("name", ""), "Mapper": song.get("mapper") or "",
            "MapType": next((name for bit, name in TYPE_BITS if int(d.get("type") or 0) & bit), ""),
            "Duration": round(float(d.get("duration") or song.get("duration") or 0), 2), "PeakEbpm": 0,
            "Njs": round(float(d.get("njs") or 0), 2), "Nps": round(float(d.get("nps") or 0), 2),
            "RankedBeatLeader": False, "RankedScoreSaber": False, "RankedAccSaber": False,
            "Ratings": r, "PpCurves": {}, "PeerPopularity": {}}


def bl_ranked():
    """{(HASH, diff): (song, difficulty, leaderboard id, ranked unix time)} of BeatLeader's ranked Standard maps."""
    out, page = {}, 1
    while True:
        d = get(BL_RANKED.format(page))
        for x in d["data"]:
            diff = x["difficulty"]
            if diff.get("modeName") == "Standard":
                out[(x["song"]["hash"].upper(), diff["difficultyName"])] = (x["song"], diff, x["id"], diff.get("rankedTime") or 0)
        if page * 100 >= d["metadata"]["total"]:
            break
        page += 1
    return out


def unix(iso):
    try:
        return int(datetime.fromisoformat(iso.replace("Z", "+00:00")).timestamp())
    except (ValueError, AttributeError):
        return 0


def latest_waypoints():
    """{(path, player): waypoint} of the training-set players' latest exploration-04 snapshot (build popularity rule)."""
    d = out_dir("04_waypoints")
    snaps = pd.read_csv(os.path.join(d, "path_snapshots.csv"), dtype={"PlayerId": str}, parse_dates=["t"])
    wps = pd.read_csv(os.path.join(d, "waypoints.csv"))
    snaps = snaps.sort_values("t").groupby(["PlayerId", "path"]).tail(1)
    out = {}
    for path, w in wps.groupby("path"):
        s = snaps[snaps.path == path]
        wp = np.searchsorted(w.path_pp_lo.to_numpy(), s.path_pp.to_numpy(), side="right") - 1
        out.update({(path, p): int(x) for p, x in zip(s.PlayerId, wp) if x >= 0})
    return out


def main():
    src = os.path.join(MODEL_DIR, "model.json")
    model = json.load(open(src, encoding="utf-8"))
    maps = model["Maps"]
    by_key = {(m["Hash"], m["Difficulty"]): m for m in maps}
    was_ranked = {k for k, m in by_key.items() if m["RankedBeatLeader"] or m["RankedScoreSaber"] or m["RankedAccSaber"]}

    print("fetching ranked / curated lists", flush=True)
    bl = bl_ranked()
    fetch_ss_leaderboards(out_dir("14_scoresaber"))
    ss = {(x["Hash"], scoresaber_styles.DIFFICULTIES.get(x["Difficulty"], "")): x
          for x in json.load(open(os.path.join(out_dir("14_scoresaber"), "leaderboards.json"), encoding="utf-8"))
          if x["GameMode"] == "SoloStandard"}
    acc_list = get(ACCSABER)
    acc = {(d["songHash"].upper(), accsaber_tracks.DIFFICULTIES.get(d["difficulty"], "")): d for d in acc_list
           if d.get("categoryCode") in accsaber_tracks.TRACKS}
    curated = {m["Hash"]: m for m in curated_list()}
    print(f"BeatLeader {len(bl)}, ScoreSaber {len(ss)}, AccSaber {len(acc)} ranked; {len(curated)} curated songs")

    # Ratings for maps ranked on SS / AccSaber only, or curated, that the model doesn't have yet (per song, cached).
    wanted = {k for k in set(ss) | set(acc) if k not in by_key and k not in bl}
    curated_keys = {(h, diff) for h, m in curated.items() for diff in m["Diffs"]} - set(by_key) - set(bl)
    songs = {}
    pool_raw, cur_raw = out_dir(os.path.join("08_pool_ratings", "raw")), out_dir(os.path.join("09_curated", "raw"))
    todo = sorted({(h, pool_raw) for h, _ in wanted} | {(h, cur_raw) for h, _ in curated_keys})
    with ThreadPoolExecutor(WORKERS) as pool:
        for (h, _), data in zip(todo, pool.map(lambda t: fetch_song(*t), todo)):
            for lb in (data or {}).get("leaderboards", []):
                d = lb.get("difficulty", {})
                if d.get("modeName") == "Standard":
                    songs[(h, d["difficultyName"])] = (data.get("song", {}) | {"hash": h}, d, lb.get("id"))

    # New entries (ranked or curated, not in the model yet).
    added = []
    for key in sorted((set(bl) | wanted | curated_keys) - set(by_key)):
        song, d, lb_id = (bl[key][:3] if key in bl else songs.get(key, (None, None, None)))
        r = all_ratings(d) if d else None
        if r is None or not build_model.has_ratings(r):
            continue
        e = entry(song, d, r)
        by_key[key] = e
        added.append(e)
        e["_lb"] = lb_id

    # Flags and platform values on every map.
    for key, m in by_key.items():
        m["RankedBeatLeader"] = key in bl
        m["RankedScoreSaber"] = key in ss
        m["RankedAccSaber"] = key in acc
        if key in ss:
            m["ScoreSaberStars"] = float(ss[key]["Stars"])
        else:
            m.pop("ScoreSaberStars", None)
        if key in acc:
            m["AccSaberCategory"], m["AccSaberComplexity"] = acc[key]["categoryCode"], float(acc[key]["complexity"])
        else:
            m.pop("AccSaberCategory", None), m.pop("AccSaberComplexity", None)
        c = curated.get(key[0])
        m["Curated"] = bool(c and key[1] in c["Diffs"])
        if m["Curated"]:
            m["BeatSaverScore"] = c["Score"]
        else:
            m.pop("BeatSaverScore", None)
    ranked = lambda m: m["RankedBeatLeader"] or m["RankedScoreSaber"] or m["RankedAccSaber"]
    dropped = [k for k, m in by_key.items() if not ranked(m) and not m["Curated"]]
    for k in dropped:
        del by_key[k]
    fresh = [m for k, m in by_key.items() if ranked(m) and k not in was_ranked]
    print(f"{len(added)} maps added, {len(fresh)} newly ranked, {len(dropped)} dropped")

    # Newly ranked maps: pp curves, peer curves, popularity.
    curve_for = build_model.knn_curves([m for m in maps if m["PpCurves"]])
    for m in fresh:
        m["PpCurves"] = {mod: curve_for(r) for mod, r in m["Ratings"].items()}
        m.pop("PeerTargets", None)
        m["PeerPopularity"] = {}
    ranked_time = {k: v[3] for k, v in bl.items()}
    for k, x in ss.items():
        ranked_time[k] = min(ranked_time.get(k) or 2**40, unix(x.get("RankedAt", "")) or 2**40)
    for k, d in acc.items():
        ranked_time[k] = min(ranked_time.get(k) or 2**40, unix(d.get("rankedAt", "")) or 2**40)
    add_peers_and_popularity(fresh, bl, songs, acc, ranked_time, model)

    for m in by_key.values():
        m.pop("_lb", None)
    model["Maps"] = list(by_key.values())
    model["UpdatedAt"] = datetime.now(timezone.utc).isoformat()
    backup = os.path.join(MODEL_DIR, f"model.{datetime.now():%Y-%m-%d-%H%M}.json.gz")
    shutil.copy2(os.path.join(MODEL_DIR, "model.json.gz"), backup)
    text = json.dumps(model, separators=(",", ":"))
    open(src, "w", encoding="utf-8").write(text)
    with gzip.open(os.path.join(MODEL_DIR, "model.json.gz"), "wt", encoding="utf-8") as f:
        f.write(text)
    print(f"maps {len(model['Maps'])}, {len(text) / 1e6:.1f} MB raw; previous model kept as {os.path.basename(backup)}")


def add_peers_and_popularity(fresh, bl, songs, acc, ranked_time, model):
    if not fresh:
        return
    d = out_dir("12_peer_scores")
    levels_path, edges_path = os.path.join(d, "levels.csv.gz"), os.path.join(d, "band_edges.json")
    if not os.path.exists(levels_path):
        print("no levels.csv.gz (run a full build_model.py first) - new maps get no peer targets / popularity")
        return
    levels = {c: g.set_index("player").level for c, g in pd.read_csv(levels_path, dtype={"player": str}).groupby("cell")}
    grids = {c: np.quantile(l.to_numpy(), peer_targets.LEVEL_QUANTILES) for c, l in levels.items()}
    edges = json.load(open(edges_path, encoding="utf-8"))
    waypoints = latest_waypoints()
    raw = out_dir(os.path.join("12_peer_scores", "raw"))

    def lb_id(m):
        key = (m["Hash"], m["Difficulty"])
        found = (bl.get(key) or (None, None, None))[2] or m.get("_lb") or (acc.get(key) or {}).get("blLeaderboardId") \
            or (songs.get(key) or (None, None, None))[2]
        if found:
            return found
        # e.g. a curated-only map now ranked on ScoreSaber: its song's leaderboards (cached per song).
        for raw_dir in (out_dir(os.path.join("09_curated", "raw")), out_dir(os.path.join("08_pool_ratings", "raw"))):
            for lb in (fetch_song(m["Hash"], raw_dir) or {}).get("leaderboards", []):
                d = lb.get("difficulty", {})
                if d.get("modeName") == "Standard" and d.get("difficultyName") == m["Difficulty"]:
                    return lb.get("id")
        return None

    ids = {id(m): lb_id(m) for m in fresh}
    with ThreadPoolExecutor(WORKERS) as pool:
        list(pool.map(lambda lb: fetch_scores(lb, raw), sorted({i for i in ids.values() if i})))

    with_peers = with_pop = 0
    for m in fresh:
        path = os.path.join(raw, f"{ids[id(m)]}.json") if ids[id(m)] else None
        if not path or not os.path.exists(path):
            continue
        rows = pd.DataFrame(json.load(open(path, encoding="utf-8")), columns=["player", "acc", "mods", "time"])
        rows["player"] = rows.player.astype(str)
        comparable = rows[rows.mods.map(peer_targets.comparable)][["player", "acc"]]
        r = m["Ratings"][""]
        one = pd.DataFrame([{"PassRating": r["Pass"], "TechRating": r["Tech"], "Stars": r["Stars"]}])
        cells = []
        for style in DESTINATIONS:
            sub, axis = path_plays(one, style)
            if len(sub):
                cells += [style, f"{style}|{int(np.searchsorted(edges[style], sub[axis].iloc[0], side='right'))}"]
        code = m.get("AccSaberCategory")
        if code in accsaber_tracks.TRACKS and accsaber_tracks.TRACKS[code] in edges:
            track = accsaber_tracks.TRACKS[code]
            cells += [track, f"{track}|{int(np.searchsorted(edges[track], m['AccSaberComplexity'], side='right'))}"]
        for cell in cells:
            if cell in levels and (curve := peer_targets.map_curve(comparable, levels[cell], grids[cell])) is not None:
                m.setdefault("PeerTargets", {})[cell] = curve
        with_peers += bool(m.get("PeerTargets"))

        # Popularity: training-set players' scores set after ranking, at their latest milestone per style.
        since = ranked_time.get((m["Hash"], m["Difficulty"])) or 0
        after = rows[rows.time >= since]
        for style in DESTINATIONS:
            if len(path_plays(one, style)[0]) == 0:
                continue
            counts = pd.Series([waypoints.get((style, p)) for p in after.player]).dropna().astype(int).value_counts()
            for wp, n in counts.items():
                m["PeerPopularity"][f"{style}|{wp}"] = int(n)
        with_pop += bool(m["PeerPopularity"])

    bl_paths = [p for p in model["Paths"] if p["Destination"] in DESTINATIONS]
    ss_ranks = {p["Destination"][len(scoresaber_styles.PREFIX):]: [w["EquivalentRank"] for w in p["Waypoints"]]
                for p in model["Paths"] if p["Destination"].startswith(scoresaber_styles.PREFIX)}
    scoresaber_styles.popularity(fresh, bl_paths, ss_ranks)
    print(f"newly ranked: {with_peers} with peer targets, {with_pop} with popularity")


if __name__ == "__main__":
    main()
