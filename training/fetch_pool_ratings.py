"""BeatLeader ratings for ScoreSaber / AccSaber ranked maps that are not BeatLeader-ranked (D-041).

BeatLeader rates every map, ranked or not: GET /leaderboards/hash/{hash} returns each difficulty's stars, pass, acc,
tech, predicted acc and speed-modifier ratings (verified 2026-11-10, anonymous). One call per song, cached per hash in
data/explore/08_pool_ratings/raw (resumable), throttled under BeatLeader's 50 / 10 s limit.
Output: data/explore/08_pool_ratings/pool_ratings.json, consumed by build_model.py.
Run from training/:  .venv/Scripts/python fetch_pool_ratings.py
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor

import pandas as pd

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "explore"))
from common import out_dir  # noqa: E402

API = "https://api.beatleader.com/leaderboards/hash/{}"
USER_AGENT = "SkillProgression-model-build (github: SkillProgression)"
DELAY = 1.0  # per worker; responses take seconds, so WORKERS x 1 / (response + 1 s) stays under 5 / s
WORKERS = 4


def fetch(hash_: str, raw_dir: str) -> dict | None:
    path = os.path.join(raw_dir, f"{hash_}.json")
    if os.path.exists(path):
        try:
            return json.load(open(path, encoding="utf-8"))
        except json.JSONDecodeError:
            os.remove(path)  # partly written by an interrupted run
    for attempt in range(4):
        try:
            req = urllib.request.Request(API.format(hash_), headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=30) as r:
                data = json.loads(r.read().decode("utf-8"))
            json.dump(data, open(path, "w", encoding="utf-8"))
            time.sleep(DELAY)
            return data
        except urllib.error.HTTPError as e:
            if e.code == 404:
                json.dump({}, open(path, "w", encoding="utf-8"))
                return {}
            wait = int(e.headers.get("Retry-After", 10)) if e.code == 429 else 5 * (attempt + 1)
            time.sleep(wait)
        except (urllib.error.URLError, TimeoutError):
            time.sleep(5 * (attempt + 1))
    return None


def ratings(d: dict, prefix: str = "") -> dict | None:
    key = (lambda k: prefix + k[0].upper() + k[1:]) if prefix else (lambda k: k)
    vals = {"Stars": d.get(key("stars")), "Pass": d.get(key("passRating")), "Acc": d.get(key("accRating")),
            "Tech": d.get(key("techRating")), "PredictedAcc": d.get(key("predictedAcc"))}
    return None if any(v is None for v in vals.values()) or not vals["Stars"] else {k: round(float(v), 5) for k, v in vals.items()}


def main():
    pools = pd.read_csv(os.path.join(out_dir("05_pools"), "pools.csv"))
    wanted = pools[(~pools.BL) & (pools.SS | pools.AS) & (pools["mode"] == "Standard")]
    wanted_keys = {(h.upper(), d) for h, d in zip(wanted.hash, wanted["diff"])}
    flags = {(h.upper(), d): (bool(ss), bool(as_)) for h, d, ss, as_ in zip(wanted.hash, wanted["diff"], wanted.SS, wanted.AS)}
    raw_dir = out_dir(os.path.join("08_pool_ratings", "raw"))
    hashes = sorted({h for h, _ in wanted_keys})
    with ThreadPoolExecutor(WORKERS) as pool:
        for i, _ in enumerate(pool.map(lambda h: fetch(h, raw_dir), hashes), 1):
            if i % 100 == 0:
                print(f"{i}/{len(hashes)} songs", flush=True)
    maps, missing = [], 0
    for h in hashes:
        data = fetch(h, raw_dir)
        if not data:
            missing += 1
            continue
        song = data.get("song", {})
        for lb in data.get("leaderboards", []):
            d = lb.get("difficulty", {})
            key = (h, d.get("difficultyName"))
            if d.get("modeName") != "Standard" or key not in wanted_keys:
                continue
            base = ratings(d)
            if base is None:
                continue
            r = {"": base}
            mr = d.get("modifiersRating") or {}
            for code in ("SS", "FS", "SF"):
                m = ratings(mr, code.lower())
                if m is not None:
                    r[code] = m
            ss, as_ = flags[key]
            maps.append({"Hash": h, "Difficulty": key[1], "Name": song.get("name", ""), "Mapper": song.get("mapper") or "",
                         "Type": int(d.get("type") or 0),
                         "Duration": float(d.get("duration") or song.get("duration") or 0),
                         "RankedScoreSaber": ss, "RankedAccSaber": as_, "Ratings": r})
    out = os.path.join(out_dir("08_pool_ratings"), "pool_ratings.json")
    json.dump(maps, open(out, "w", encoding="utf-8"))
    print(f"{len(maps)} of {len(wanted_keys)} difficulties rated ({missing} songs unavailable) -> {out}")


if __name__ == "__main__":
    main()
