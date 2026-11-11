"""Exploration 06: leaning scores — P(destination | style) for players who have not arrived yet.

Trained on quarterly snapshots (exploration 02) of players who later arrived at a destination (rules v6). Style features
are made level-relative first: each is compared with the population median at the same level (log of top-30 pp sum),
because raw style is confounded by what a player can play (e.g. slow maps have tech > pass naturally).
Exports the fitted parameters (scaler + multinomial logistic regression) as JSON — aggregate parameters only, the
kind of artefact that may be published (D-007). Outputs: data/explore/06_leaning/.
"""
import json

import numpy as np
import pandas as pd
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import balanced_accuracy_score, log_loss
from sklearn.model_selection import GroupKFold, cross_val_predict
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler

from common import out_dir
from profiles import DESTINATIONS, UNSPECIALISED, destination

OUT = out_dir("06_leaning")


LEVEL_BINS = 25


def style_columns(df):
    return [c for c in df.columns if c.startswith("s_")]


def peer_baseline(pop: pd.DataFrame, feats) -> dict:
    """Median of each style feature by level (quantile bins of log top-30 pp sum) over the whole population."""
    level = np.log(pop.top_pp_sum.clip(lower=1))
    edges = np.unique(np.quantile(level, np.linspace(0, 1, LEVEL_BINS + 1)))
    centers, medians = [], {f: [] for f in feats}
    for lo, hi in zip(edges[:-1], edges[1:]):
        b = pop[(level >= lo) & (level <= hi)]
        centers.append(float((lo + hi) / 2))
        for f in feats:
            medians[f].append(float(b[f].median()))
    return {"level": "log(top30 pp sum)", "centers": centers, "medians": medians}


def relative(df: pd.DataFrame, feats, base) -> np.ndarray:
    """Peer-relative style features. Tried raw + relative (better CV on climbers: 0.80 vs 0.76) but it re-introduces
    the level confound: 68-80% of today's rank 10-50k Unspecialised 'lean High Tech' because slow maps have
    tech > pass. Relative-only is kept; low-level leanings are low-confidence hints (see PROGRESSION_DESIGN 06)."""
    level = np.log(df.top_pp_sum.clip(lower=1)).to_numpy()
    return np.column_stack([df[f].to_numpy() - np.interp(level, base["centers"], base["medians"][f]) for f in feats])


def main():
    snaps = pd.read_csv(f"{out_dir('02_trajectories')}/snapshots.csv", dtype={"PlayerId": str})
    snaps = snaps[snaps.final.isin(DESTINATIONS)].reset_index(drop=True)
    feats = style_columns(snaps)
    cur = pd.read_csv(f"{out_dir('03_thresholds')}/current_profiles.csv", dtype={"PlayerId": str})
    base = peer_baseline(cur, feats)
    X, y, groups = relative(snaps, feats, base), snaps.final.to_numpy(), snaps.PlayerId.to_numpy()
    model = make_pipeline(StandardScaler(), LogisticRegression(max_iter=3000, class_weight="balanced"))
    proba = cross_val_predict(model, X, y, groups=groups, cv=GroupKFold(5), method="predict_proba")
    classes = np.array(sorted(set(y)))
    pred = classes[proba.argmax(1)]
    rep = [f"# Exploration 06 — leaning scores\n\nTraining snapshots: {len(snaps):,} from {snaps.PlayerId.nunique():,} "
           f"players; classes {pd.Series(y).value_counts().to_dict()}\nFeatures: {feats}\n",
           f"\nOverall (5-fold, grouped by player): balanced accuracy {balanced_accuracy_score(y, pred):.3f}, "
           f"log loss {log_loss(y, proba, labels=classes):.3f} (uniform = {np.log(len(classes)):.3f})\n"]

    rows = []
    for lo, hi in [(1, 2000), (2001, 5000), (5001, 10000), (10001, 20000), (20001, 40000), (40001, 10**7)]:
        m = ((snaps.eq_rank >= lo) & (snaps.eq_rank <= hi)).to_numpy()
        if m.sum() < 100:
            continue
        conf = proba[m].max(1)
        right = pred[m] == y[m]
        rows.append({"equivalent rank": f"{lo}-{hi}", "snapshots": int(m.sum()),
                     "balanced acc": round(balanced_accuracy_score(y[m], pred[m]), 3),
                     "mean top prob": round(conf.mean(), 3),
                     "acc when top prob >= 0.6": round(right[conf >= 0.6].mean(), 3) if (conf >= 0.6).any() else None,
                     "share with top prob >= 0.6": round((conf >= 0.6).mean(), 3)})
    rep.append("\n## Calibration by level at snapshot time\n\n" + pd.DataFrame(rows).to_markdown(index=False) + "\n")

    # final model -> exported parameters
    model.fit(X, y)
    scaler, lr = model.named_steps["standardscaler"], model.named_steps["logisticregression"]
    params = {"version": 2, "features": feats, "peer_baseline": base, "classes": lr.classes_.tolist(),
              "mean": scaler.mean_.tolist(), "scale": scaler.scale_.tolist(),
              "coef": lr.coef_.tolist(), "intercept": lr.intercept_.tolist(),
              "trained_on": {"snapshots": int(len(snaps)), "players": int(snaps.PlayerId.nunique())}}
    json.dump(params, open(f"{OUT}/leaning_model.json", "w"), indent=1)
    coef = pd.DataFrame(lr.coef_.T, index=[f"rel_{f}" for f in feats], columns=lr.classes_).round(2)
    rep.append("\n## Coefficients (standardised features; positive = pushes toward that destination)\n\n"
               + coef.to_markdown() + "\n")

    # apply to today's unspecialised players
    cur["dest"] = cur.apply(destination, axis=1)
    un = cur[cur.dest == UNSPECIALISED].copy()
    p = model.predict_proba(relative(un, feats, base))
    un["leaning"] = lr.classes_[p.argmax(1)]
    un["leaning_p"] = p.max(1)
    for i, c in enumerate(lr.classes_):
        un[f"p_{c}"] = p[:, i]
    un.to_csv(f"{OUT}/unspecialised_leanings.csv", index=False)
    rows = []
    for lo, hi in [(1, 5000), (5001, 10000), (10001, 20000), (20001, 50000)]:
        b = un[(un.Rank >= lo) & (un.Rank <= hi)]
        if len(b):
            rows.append({"ranks": f"{lo}-{hi}", "unspecialised": len(b),
                         **b.leaning.value_counts(normalize=True).round(3).to_dict(),
                         "median top prob": round(b.leaning_p.median(), 3)})
    rep.append("\n## Leanings of today's Unspecialised players\n\n" + pd.DataFrame(rows).fillna(0).to_markdown(index=False) + "\n")
    open(f"{OUT}/report.md", "w", encoding="utf-8").write("".join(rep))
    print("".join(rep))


if __name__ == "__main__":
    main()
