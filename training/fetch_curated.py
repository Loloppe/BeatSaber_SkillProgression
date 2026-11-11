"""Curated BeatSaver maps with their BeatLeader ratings, for Explore's Curated pool (D-045).

BeatSaver: GET /search/text/{page}?curated=true&sortOrder=Curated&pageSize=100 (100 per page max; 13,098 maps on
2026-11-11), latest version hash, Standard difficulties, BeatSaver score. Re-fetched every run (~2 min).
BeatLeader: GET /leaderboards/hash/{hash} per song, cached per hash in data/explore/09_curated/raw (resumable, same
fetch/throttle as fetch_pool_ratings.py). Only rated difficulties are kept: BeatLeader doesn't rate modded maps.
Output: data/explore/09_curated/curated.json, consumed by build_model.py.
Run from training/:  .venv/Scripts/python fetch_curated.py
"""
import json
import os
import sys
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor

from fetch_pool_ratings import USER_AGENT, WORKERS, fetch, ratings

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "explore"))
from common import out_dir  # noqa: E402

BEATSAVER = "https://api.beatsaver.com/search/text/{}?curated=true&sortOrder=Curated&pageSize=100"


def curated_list() -> list[dict]:
    # info.pages assumes 20 per page whatever pageSize is: stop at the first empty page instead.
    maps, page = {}, 0
    while True:
        req = urllib.request.Request(BEATSAVER.format(page), headers={"User-Agent": USER_AGENT})
        with urllib.request.urlopen(req, timeout=30) as r:
            docs = json.loads(r.read().decode("utf-8"))["docs"]
        if not docs:
            break
        for m in docs:
            version = next((v for v in m.get("versions", []) if v.get("state") == "Published"), None) or m["versions"][0]
            diffs = [d["difficulty"] for d in version["diffs"] if d["characteristic"] == "Standard"]
            if diffs:
                maps[version["hash"].upper()] = {"Hash": version["hash"].upper(), "Key": m["id"], "Diffs": diffs,
                                                 "Score": float(m.get("stats", {}).get("score") or 0)}
        page += 1
        if page % 20 == 0:
            print(f"BeatSaver {page} pages", flush=True)
        time.sleep(0.5)
    return list(maps.values())


def main():
    listing = curated_list()
    json.dump(listing, open(os.path.join(out_dir("09_curated"), "beatsaver.json"), "w", encoding="utf-8"))
    print(f"{len(listing)} curated songs with Standard difficulties", flush=True)

    raw_dir = out_dir(os.path.join("09_curated", "raw"))
    hashes = sorted({m["Hash"] for m in listing})
    with ThreadPoolExecutor(WORKERS) as pool:
        for i, _ in enumerate(pool.map(lambda h: fetch(h, raw_dir), hashes), 1):
            if i % 250 == 0:
                print(f"BeatLeader {i}/{len(hashes)} songs", flush=True)

    maps, unrated, missing = [], 0, 0
    for m in listing:
        data = fetch(m["Hash"], raw_dir)
        if not data:
            missing += 1
            continue
        song = data.get("song", {})
        for lb in data.get("leaderboards", []):
            d = lb.get("difficulty", {})
            if d.get("modeName") != "Standard" or d.get("difficultyName") not in m["Diffs"]:
                continue
            base = ratings(d)
            if base is None:
                unrated += 1
                continue
            r = {"": base}
            mr = d.get("modifiersRating") or {}
            for code in ("SS", "FS", "SF"):
                mod = ratings(mr, code.lower())
                if mod is not None:
                    r[code] = mod
            maps.append({"Hash": m["Hash"], "Difficulty": d["difficultyName"], "Name": song.get("name", ""),
                         "Mapper": song.get("mapper") or "",
                         "Type": int(d.get("type") or 0), "Duration": float(d.get("duration") or song.get("duration") or 0),
                         "BeatSaverScore": m["Score"], "Ratings": r})
    out = os.path.join(out_dir("09_curated"), "curated.json")
    json.dump(maps, open(out, "w", encoding="utf-8"))
    print(f"{len(maps)} rated difficulties ({unrated} unrated skipped, {missing} songs unavailable) -> {out}")


if __name__ == "__main__":
    main()
