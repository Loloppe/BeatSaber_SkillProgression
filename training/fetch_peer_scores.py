"""Every BeatLeader score on the ScoreSaber / AccSaber ranked maps that are not BeatLeader-ranked (D-058 peer targets).

GET /leaderboard/{id}/scoregraph returns all scores of a leaderboard (player, accuracy, modifiers, date; verified
2026-11-14, anonymous, also on unranked maps). Leaderboard ids come from the /leaderboards/hash responses cached by
fetch_pool_ratings.py (run it first). One call per map, cached per leaderboard in data/explore/12_peer_scores/raw as a
compact list [playerId, accuracy (0..1), modifiers, timepost] (resumable). BL-ranked maps need no fetch: the population
DB has their scores.
Local only (D-007): these are per-player records and never leave data/.
Run from training/:  .venv/Scripts/python fetch_peer_scores.py
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "explore"))
from common import out_dir  # noqa: E402

API = "https://api.beatleader.com/leaderboard/{}/scoregraph"
USER_AGENT = "SkillProgression-model-build (github: SkillProgression)"
DELAY = 1.0  # per worker, as in fetch_pool_ratings.py
WORKERS = 3


def fetch(lb_id: str, raw_dir: str) -> bool:
    path = os.path.join(raw_dir, f"{lb_id}.json")
    if os.path.exists(path):
        return True
    for attempt in range(4):
        try:
            req = urllib.request.Request(API.format(lb_id), headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=120) as r:
                data = json.loads(r.read().decode("utf-8"))
            rows = [[s["playerId"], round(s["accuracy"] / 100, 6), s.get("modifiers") or "", s.get("timepost") or 0]
                    for s in data]
            tmp = path + ".tmp"
            json.dump(rows, open(tmp, "w", encoding="utf-8"))
            os.replace(tmp, path)  # never leave a partly written file behind
            time.sleep(DELAY)
            return True
        except urllib.error.HTTPError as e:
            if e.code == 404:
                json.dump([], open(path, "w", encoding="utf-8"))
                return True
            time.sleep(int(e.headers.get("Retry-After", 10)) if e.code == 429 else 5 * (attempt + 1))
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError):
            time.sleep(5 * (attempt + 1))
    return False


def leaderboard_ids() -> dict:
    """{(HASH, difficulty): leaderboard id} for the pool maps of fetch_pool_ratings.py (Standard only)."""
    maps = json.load(open(os.path.join(out_dir("08_pool_ratings"), "pool_ratings.json"), encoding="utf-8"))
    raw = out_dir(os.path.join("08_pool_ratings", "raw"))
    ids = {}
    for h in sorted({m["Hash"] for m in maps}):
        wanted = {m["Difficulty"] for m in maps if m["Hash"] == h}
        data = json.load(open(os.path.join(raw, f"{h}.json"), encoding="utf-8"))
        for lb in data.get("leaderboards", []):
            d = lb.get("difficulty", {})
            if d.get("modeName") == "Standard" and d.get("difficultyName") in wanted:
                ids[(h, d["difficultyName"])] = lb["id"]
    return ids


def main():
    ids = leaderboard_ids()
    raw_dir = out_dir(os.path.join("12_peer_scores", "raw"))
    todo = sorted(set(ids.values()))
    with ThreadPoolExecutor(WORKERS) as pool:
        failed = 0
        for i, ok in enumerate(pool.map(lambda lb: fetch(lb, raw_dir), todo), 1):
            failed += not ok
            if i % 100 == 0:
                print(f"{i}/{len(todo)} maps", flush=True)
    index = [{"Hash": h, "Difficulty": d, "LeaderboardId": lb} for (h, d), lb in sorted(ids.items())]
    json.dump(index, open(os.path.join(out_dir("12_peer_scores"), "index.json"), "w", encoding="utf-8"))
    print(f"{len(todo) - failed} of {len(todo)} maps cached ({failed} failed, rerun to retry) -> {raw_dir}")


if __name__ == "__main__":
    main()
