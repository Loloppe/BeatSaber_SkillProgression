"""Shared loaders for exploration scripts (stage 1 of D-017). Read-only access to local data sources."""
import gzip
import json
import os
import sqlite3

import numpy as np
import pandas as pd

REPOS = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
POPULATION_DB = os.path.join(REPOS, "portaBLerebalancer2", "wwwroot", "db-20261104211323-3944.db")  # D-015
ATTEMPTS_DIR = os.path.join(REPOS, "portaBLeRebalancer", "wwwroot", "attempts")
OUT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "data", "explore"))


def anchors() -> dict:
    """User-provided anchor players {blId: destination} from data/anchors.json (local only, never committed)."""
    path = os.path.join(os.path.dirname(OUT_ROOT), "anchors.json")
    return json.load(open(path, encoding="utf-8")).get("anchors", {}) if os.path.exists(path) else {}


def out_dir(name: str) -> str:
    path = os.path.join(OUT_ROOT, name)
    os.makedirs(path, exist_ok=True)
    return path


def connect() -> sqlite3.Connection:
    return sqlite3.connect(f"file:{POPULATION_DB}?mode=ro", uri=True)


def load_leaderboards(con) -> pd.DataFrame:
    return pd.read_sql_query(
        """select Id, Name, Mapper, Hash, DifficultyName, ModeName, Stars, PassRating, AccRating, TechRating, PredictedAcc,
                  MapType, Duration, Njs, NoteCount, NoteSpan, PeakEbpm, LinearPercent, MultiPercentage,
                  HardSwingRatio, HitDistance, ParityErrors, BombAvoidances, CrouchWalls, DodgeWalls, Count,
                  ModifiersRatingId
           from Leaderboards""", con)


def load_players(con) -> pd.DataFrame:
    return pd.read_sql_query("select Id, Pp, AccPp, PassPp, TechPp, Rank, RankedPlayCount from Players", con)


def load_scores(con) -> pd.DataFrame:
    return pd.read_sql_query(
        """select PlayerId, LeaderboardId, Accuracy, Pp, AccPP, PassPP, TechPP, Weight, Modifiers, FC, Timepost
           from Scores where Pp > 0""", con)


_DTYPES = {"i4": "<i4", "u1": "u1", "f4": "<f4", "u2": "<u2"}
_SIZES = {"i4": 4, "u1": 1, "f4": 4, "u2": 2}
ATTEMPT_TYPES = {0: "Unknown", 1: "Clear", 2: "Fail", 3: "Restart", 4: "Quit", 5: "Practice"}


def load_attempts(columns=None) -> tuple[pd.DataFrame, dict]:
    """Columnar attempts export (see portaBLeRebalancer/Refresh/Tools/AttemptsExport.cs).
    Player/Leaderboard/Modifiers are indices into meta['Players'|'Leaderboards'|'Modifiers'].
    Raw attempts must never leave data/ (D-007)."""
    meta = json.load(open(os.path.join(ATTEMPTS_DIR, "attempts.json"), encoding="utf-8"))
    cols = meta["Columns"]
    wanted = columns or [c["Name"] for c in cols]
    parts = []
    for chunk in meta["Chunks"]:
        n = chunk["Rows"]
        raw = gzip.open(os.path.join(ATTEMPTS_DIR, chunk["File"])).read()
        off, data = 0, {}
        for c in cols:
            if c["Name"] in wanted:
                data[c["Name"]] = np.frombuffer(raw, dtype=_DTYPES[c["Type"]], count=n, offset=off).copy()
            off += _SIZES[c["Type"]] * n
        parts.append(pd.DataFrame(data))
        del raw
    return pd.concat(parts, ignore_index=True), meta
