"""Prototype profile features (stage 1 of D-017). Candidates for the C# Core implementation once validated."""
import numpy as np
import pandas as pd

TOP_N = 30  # plays per player that define the profile
SPEED_MODS = ("SS", "FS", "SF")  # priority order: later wins (SF > FS > SS)
RATING_COLS = ("Stars", "PassRating", "AccRating", "TechRating", "PredictedAcc")


def speed_mod(mods: str) -> str:
    """Speed modifier that determines the effective ratings of a play ('' = none)."""
    parts = (mods or "").split(",")
    found = ""
    for code in SPEED_MODS:
        if code in parts:
            found = code
    return found


def apply_modifier_ratings(plays: pd.DataFrame, mods: pd.DataFrame) -> pd.DataFrame:
    """Rate each play with the ratings of the speed modifier actually used, not the nomod ratings.
    Extreme players earn most of their pp with FS/SF; nomod ratings hide that (user feedback 2026-11-10)."""
    plays = plays.merge(mods, left_on="ModifiersRatingId", right_on="Id", how="left", suffixes=("", "_mr"))
    sm = plays.Modifiers.fillna("").map(speed_mod)
    plays["SpeedMod"] = sm
    for code in SPEED_MODS:
        has = (sm == code) & plays[f"{code}Stars"].notna()
        for col in RATING_COLS:
            plays.loc[has, col] = plays.loc[has, f"{code}{col}"]
    return plays


def _wavg(x, w):
    return float(np.average(x, weights=w)) if w.sum() > 0 else float(np.mean(x))


def _share(w, flag, band):
    """pp share of plays with `flag` among plays in `band` (0 when the band is empty)."""
    tot = w[band].sum()
    return float(w[band & flag].sum() / tot) if tot > 0 else 0.0


def _wmedian(x, w):
    x, w = np.asarray(x, float), np.asarray(w, float)
    order = np.argsort(x)
    cw = np.cumsum(w[order])
    return float(x[order][np.searchsorted(cw, cw[-1] / 2)])


def profile(top: pd.DataFrame, pass_ceiling: float) -> dict:
    """Features of one player from their top plays (already sorted by Pp desc, at most TOP_N rows).
    Needs columns: Pp, Accuracy, Stars, PassRating, AccRating, TechRating, PredictedAcc, MapType, Duration,
    PeakEbpm, SpeedMod; optional AccPP/PassPP/TechPP."""
    w = top.Pp.to_numpy()
    typ = top.MapType.to_numpy()
    stars = top.Stars.to_numpy()
    out = {
        # level descriptors
        "stars": _wavg(stars, w), "pass": _wavg(top.PassRating, w), "acc_rating": _wavg(top.AccRating, w),
        "tech": _wavg(top.TechRating, w), "acc": _wavg(top.Accuracy, w), "pass_ceiling": pass_ceiling,
        "duration": _wavg(top.Duration, w), "ebpm": _wavg(top.PeakEbpm, w),
        "top_pp_sum": float(w.sum()),
        "pass_med": _wmedian(top.PassRating, w), "tech_med": _wmedian(top.TechRating, w),
        "stars_med": _wmedian(stars, w),
        # destination shares (D-018)
        "share_under5": w[stars < 5].sum() / w.sum(),
        "share_5to9": w[(stars >= 5) & (stars < 9)].sum() / w.sum(),
        "share_9to13": w[(stars >= 9) & (stars < 13)].sum() / w.sum(),
        "share_13plus": w[stars >= 13].sum() / w.sum(),
        "tech_share_5to9": _share(w, typ == "tech", (stars >= 5) & (stars < 9)),
        "tech_share_9plus": _share(w, typ == "tech", stars >= 9),
        "tech_share_5plus": _share(w, typ == "tech", stars >= 5),
        # level-independent style features
        "s_below_ceiling": 1.0 - _wavg(top.PassRating, w) / max(pass_ceiling, 1e-6),
        "s_tech_vs_pass": _wavg(top.TechRating, w) / max(_wavg(top.PassRating, w), 1e-6),
        "s_acc_resid": _wavg(top.Accuracy - top.PredictedAcc, w),
        "s_type_acc": w[typ == "acc"].sum() / w.sum(),
        "s_type_tech": w[typ == "tech"].sum() / w.sum(),
        "s_type_speed": w[typ == "speed"].sum() / w.sum(),
        "s_star_spread": float(np.std(stars) / max(np.mean(stars), 1e-6)),
        "s_speed_mods": w[(top.SpeedMod != "").to_numpy()].sum() / w.sum(),
        "s_long_maps": w[top.Duration.to_numpy() > 240].sum() / w.sum(),
        "s_sub95": w[top.Accuracy.to_numpy() < 0.95].sum() / w.sum(),
    }
    if {"AccPP", "PassPP", "TechPP"} <= set(top.columns):
        pp = top[["AccPP", "PassPP", "TechPP"]].sum()
        tot = pp.sum() or 1.0
        out["s_pp_acc"], out["s_pp_tech"] = pp.AccPP / tot, pp.TechPP / tot
    return out


DESTINATIONS = ("Accuracy", "Midspeed", "High Tech", "Speed", "Extreme")  # D-021 names
UNSPECIALISED = "Unspecialised"

THRESHOLDS = dict(  # rules v6 (D-020, D-021). Pass + tech ratings; Extreme by effective stars.
    acc_resid=-0.01,     # acc vs BL predicted acc needed for Accuracy / Midspeed
    low_pass=3.0,        # Accuracy: typical top-play pass <= this
    mid_pass=8.0,        # Midspeed: typical top-play pass <= this (includes the high end of midspeed, D-021)
    mid_gap=1.0,         # Midspeed "choice": pass ceiling >= typical pass + this
    tech_ratio=0.8,      # tech-led: typical tech >= tech_ratio * typical pass ...
    tech_min=5.0,        # ... and typical tech >= this (slow maps have tech > pass naturally)
    tech_arrive=6.0,     # High Tech arrival: typical tech >= this
    speed_arrive=8.0,    # Speed arrival: typical pass >= this
    extreme_stars=13.0,  # Extreme: typical top-play effective stars >= this (high pass AND tech, user def.)
    extreme_path_min_stars=10.0,  # Extreme path only counts maps this hard (review round 3: all maps gave D=3.6★)
)


def destination(p, **overrides) -> str:
    """Rule-based arrival at a destination (D-018, D-020, D-021); 'Unspecialised' = none yet.
    'Typical' = pp-weighted median over the top plays; all ratings modifier-adjusted.
    Slow tech acc counts as the High Tech path (early phase), not Midspeed."""
    t = {**THRESHOLDS, **overrides}
    pas, tech, acc = p["pass_med"], p["tech_med"], p["s_acc_resid"]
    tech_led = tech >= t["tech_ratio"] * pas and tech >= t["tech_min"]
    if p["stars_med"] >= t["extreme_stars"]:
        return "Extreme"
    if acc >= t["acc_resid"]:
        if pas <= t["low_pass"]:
            return "Accuracy"
        if pas <= t["mid_pass"]:
            if tech_led:
                return "High Tech"  # slow tech acc phase
            if p["pass_ceiling"] >= pas + t["mid_gap"]:
                return "Midspeed"
    if tech_led and tech >= t["tech_arrive"]:
        return "High Tech"
    if not tech_led and pas >= t["speed_arrive"]:
        return "Speed"
    return UNSPECIALISED


# --- Paths (D-022): which plays count for a path, and the rating axis that measures difficulty on it -------------
def map_tech_led(pass_rating, tech_rating, t=THRESHOLDS):
    return (tech_rating >= t["tech_ratio"] * pass_rating) & (tech_rating >= t["tech_min"])


def path_plays(plays: pd.DataFrame, path: str, t=THRESHOLDS) -> tuple[pd.DataFrame, str]:
    """(plays relevant to `path`, axis column). Plays need effective PassRating/TechRating/Stars."""
    tech_led = map_tech_led(plays.PassRating, plays.TechRating, t)
    if path == "Speed":
        return plays[~tech_led & (plays.PassRating > t["low_pass"])], "PassRating"
    if path == "High Tech":
        return plays[tech_led], "TechRating"
    if path == "Midspeed":
        return plays[~tech_led & (plays.PassRating > t["low_pass"]) & (plays.PassRating <= t["mid_pass"] + 1)], "PassRating"
    if path == "Accuracy":
        return plays[plays.PassRating <= t["low_pass"]], "Stars"
    if path == "Extreme":
        return plays[plays.Stars >= t["extreme_path_min_stars"]], "Stars"
    raise ValueError(path)


def path_state(best: pd.DataFrame, path: str, k: int = 15) -> dict | None:
    """Player's state on a path from their best play per leaderboard: path pp (orders waypoints, combines both kinds
    of improvement), difficulty reached D (typical axis rating of top-k path plays) and accuracy at that difficulty
    A (acc vs BL predicted acc on the same plays) — the 'push' and 'polish' components (D-022)."""
    plays, axis = path_plays(best, path)
    if len(plays) < 5:
        return None
    plays = plays.sort_values("Pp", ascending=False)
    top = plays.head(k)
    w = top.Pp.to_numpy()
    return {
        "path_pp": float((plays.Pp.to_numpy() * 0.965 ** np.arange(len(plays))).sum()),
        "D": _wmedian(top[axis], w),
        "A": _wavg(top.Accuracy - top.PredictedAcc, w),
        "acc": _wavg(top.Accuracy, w),
        "n_path_plays": len(plays),
    }
