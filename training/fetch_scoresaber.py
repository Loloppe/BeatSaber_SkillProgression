"""ScoreSaber data for the ScoreSaber-measured styles (D-062): ranked leaderboards (stars, modifier values), the pp
curve, the pp at each global rank milestone, and the top scores of players sampled around each milestone.

ScoreSaber API (verified 2026-11-22, API_INTEGRATIONS.md): `/api/v2/leaderboards?status=RANKED`, `/api/v2/realms/1/pp-curve`,
`/api/players?page=N` (50 per page), `/api/player/{id}/scores?sort=top&limit=100&page=N`.
Output: data/explore/14_scoresaber/{leaderboards,curve,rank_pp}.json and players/{id}.json, each a compact list
[HASH, difficulty (1..9), gameMode, accuracy (base score / max score), modifiers, pp] of ranked scores (resumable).
Local only (D-007): sampled players' scores are per-player records and never leave data/.
Run from training/:  .venv/Scripts/python fetch_scoresaber.py
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
from fetch_peer_scores import USER_AGENT  # noqa: E402

API = "https://scoresaber.com/api"
RANK_MILESTONES = [30000, 20000, 15000, 10000, 7500, 5000, 3000, 2000, 1000, 500, 250, 100, 50, 25, 10]
SAMPLE = 80          # players sampled around each milestone (nearest ranks)
SCORE_PAGES = 3      # top 300 scores per player: path pp and top-15 per style are settled well before that
WORKERS = 2
DELAY = 0.35         # per worker: ~340 calls/min, under ScoreSaber's 400/min


def get(path: str):
    for attempt in range(5):
        try:
            req = urllib.request.Request(API + path, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=60) as r:
                data = json.loads(r.read().decode("utf-8"))
            time.sleep(DELAY)
            return data
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None
            time.sleep(int(e.headers.get("Retry-After", 10)) if e.code == 429 else 5 * (attempt + 1))
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError):
            time.sleep(5 * (attempt + 1))
    raise RuntimeError(f"ScoreSaber request failed: {path}")


def leaderboards(out):
    items, page = [], 1
    while True:
        d = get(f"/v2/leaderboards?status=RANKED&limit=100&page={page}")
        items += [{"Id": x["id"], "Hash": x["map"]["hash"].upper(), "Difficulty": x["difficulty"]["difficulty"],
                   "GameMode": x["difficulty"]["gameMode"], "Stars": x["realm"]["stars"],
                   "ModifierValues": json.loads(x["realm"].get("modifierValues") or "[]"),
                   "PositiveModifiers": bool(x["realm"].get("positiveModifiers"))} for x in d["data"]]
        if page >= d["metadata"]["totalPages"]:
            break
        page += 1
    json.dump(items, open(os.path.join(out, "leaderboards.json"), "w", encoding="utf-8"))
    print(f"ScoreSaber: {len(items)} ranked leaderboards")


def player_scores(pid: str, raw: str) -> bool:
    path = os.path.join(raw, f"{pid}.json")
    if os.path.exists(path):
        return True
    rows = []
    for page in range(1, SCORE_PAGES + 1):
        d = get(f"/player/{pid}/scores?sort=top&limit=100&page={page}")
        scores = (d or {}).get("playerScores") or []
        for s in scores:
            sc, lb = s["score"], s["leaderboard"]
            if not lb.get("ranked") or not lb.get("maxScore") or sc["pp"] <= 0:
                continue
            rows.append([lb["songHash"].upper(), lb["difficulty"]["difficulty"], lb["difficulty"]["gameMode"],
                         round(sc["baseScore"] / lb["maxScore"], 6), sc.get("modifiers") or "", round(sc["pp"], 3)])
        if len(scores) < 100 or (scores and scores[-1]["score"]["pp"] <= 0):
            break
    tmp = path + ".tmp"
    json.dump(rows, open(tmp, "w", encoding="utf-8"))
    os.replace(tmp, path)
    return True


def main():
    out = out_dir("14_scoresaber")
    raw = out_dir(os.path.join("14_scoresaber", "players"))
    leaderboards(out)
    json.dump(get("/v2/realms/1/pp-curve"), open(os.path.join(out, "curve.json"), "w", encoding="utf-8"))

    total = get("/players?page=1")["metadata"]["total"]
    pages, rank_pp, samples, ranks = {}, {}, {}, {}

    def page(n):
        if n not in pages:
            pages[n] = get(f"/players?page={n}")["players"] if 1 <= n <= (total + 49) // 50 else []
        return pages[n]

    for r in [m for m in RANK_MILESTONES if m <= total]:
        p = (r - 1) // 50 + 1
        around = [x for n in (p - 1, p, p + 1) for x in page(n)]
        rank_pp[str(r)] = next(x["pp"] for x in around if x["rank"] == r)
        nearest = sorted(around, key=lambda x: abs(x["rank"] - r))[:SAMPLE]
        samples[str(r)] = [x["id"] for x in nearest]
        ranks.update({x["id"]: x["rank"] for x in nearest})
    json.dump({"Players": total, "Pp": rank_pp, "Samples": samples, "Ranks": ranks},
              open(os.path.join(out, "rank_pp.json"), "w", encoding="utf-8"), indent=1)
    print(f"ScoreSaber: {total:,} ranked players; pp at milestones {rank_pp}")

    todo = sorted({pid for ids in samples.values() for pid in ids})
    with ThreadPoolExecutor(WORKERS) as pool:
        for i, _ in enumerate(pool.map(lambda pid: player_scores(pid, raw), todo), 1):
            if i % 100 == 0:
                print(f"{i}/{len(todo)} players", flush=True)
    print(f"{len(todo)} sampled players cached -> {raw}")


if __name__ == "__main__":
    main()
