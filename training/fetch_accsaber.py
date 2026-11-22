"""AccSaber data for the category tracks (D-061): ranked difficulties (category, complexity), the score curve and the
weight curve, plus every BeatLeader score on the AccSaber maps BeatLeader ranks (the others are cached by
fetch_peer_scores.py; run it first).

AccSaber `/v1/maps/difficulties/all`, `/v1/curves`, `/v1/categories` (verified 2026-11-22, API_INTEGRATIONS.md) ->
data/explore/13_accsaber/{difficulties,curves,categories}.json, and the category AP at each track rank milestone from
`/v1/leaderboards/{code}` -> rank_ap.json (waypoints use AccSaber's own ranks: BeatLeader scores alone miss players).
BeatLeader `/leaderboard/{id}/scoregraph` for the AccSaber difficulties' `blLeaderboardId` -> data/explore/12_peer_scores/raw (same compact cache, resumable), and
13_accsaber/scores_index.json lists every AccSaber difficulty's leaderboard id.
Local only (D-007): the scores are per-player records and never leave data/.
Run from training/:  .venv/Scripts/python fetch_accsaber.py
"""
import json
import os
import sys
import urllib.request
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "explore"))
from common import out_dir  # noqa: E402
from fetch_peer_scores import USER_AGENT, WORKERS, fetch  # noqa: E402
from accsaber_tracks import RANK_MILESTONES, TRACKS  # noqa: E402

API = "https://api.accsaber.com/v1"
DIFFICULTIES = {"EASY": "Easy", "NORMAL": "Normal", "HARD": "Hard", "EXPERT": "Expert", "EXPERT_PLUS": "ExpertPlus"}


def get(path: str):
    req = urllib.request.Request(API + path, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read().decode("utf-8"))


def main():
    out = out_dir("13_accsaber")
    data = {name: get(path) for name, path in
            (("difficulties", "/maps/difficulties/all"), ("curves", "/curves"), ("categories", "/categories"))}
    for name, value in data.items():
        json.dump(value, open(os.path.join(out, f"{name}.json"), "w", encoding="utf-8"))
    rank_ap = {}
    for code in TRACKS:
        total = get(f"/leaderboards/{code}?page=0&size=1")["totalElements"]
        rank_ap[code] = {"Players": total, "Ap": {str(r): get(f"/leaderboards/{code}?page={r - 1}&size=1")["content"][0]["ap"]
                                                  for r in RANK_MILESTONES if r <= total}}
    json.dump(rank_ap, open(os.path.join(out, "rank_ap.json"), "w", encoding="utf-8"), indent=1)
    print("AP at rank milestones: " + ", ".join(f"{c} {v['Players']:,} players" for c, v in rank_ap.items()))
    diffs = data["difficulties"]
    print(f"AccSaber: {len(diffs)} ranked difficulties, {len(data['curves'])} curves, {len(data['categories'])} categories")

    index = [{"Hash": d["songHash"].upper(), "Difficulty": DIFFICULTIES[d["difficulty"]], "LeaderboardId": d["blLeaderboardId"]}
             for d in diffs if d.get("blLeaderboardId") and d.get("difficulty") in DIFFICULTIES]
    json.dump(index, open(os.path.join(out, "scores_index.json"), "w", encoding="utf-8"))
    raw_dir = out_dir(os.path.join("12_peer_scores", "raw"))
    todo = sorted({e["LeaderboardId"] for e in index})
    with ThreadPoolExecutor(WORKERS) as pool:
        failed = sum(not ok for ok in pool.map(lambda lb: fetch(lb, raw_dir), todo))
    print(f"{len(todo) - failed} of {len(todo)} AccSaber leaderboards cached ({failed} failed, rerun to retry) -> {raw_dir}")


if __name__ == "__main__":
    main()
