"""Exploration 01: do the user's archetypes emerge from top players, and where do players diverge?

Archetypes to look for (D-004): low-star acc, mid-star (midspeed or tech) acc, high tech, speed, extreme.
Profile = a player's top-N plays by pp (their demonstrated best), described with level-independent style features.
Outputs go to data/explore/01_archetypes/ (gitignored).
"""
import numpy as np
import pandas as pd
from sklearn.cluster import KMeans
from sklearn.decomposition import PCA
from sklearn.metrics import silhouette_score
from sklearn.mixture import GaussianMixture
from sklearn.preprocessing import StandardScaler

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt

from common import connect, load_leaderboards, load_players, load_scores, out_dir
from profiles import TOP_N, apply_modifier_ratings, profile

MIN_SCORES = 30     # players need at least this many ranked scores
TOP_PLAYERS = 2000  # population used to discover archetypes
OUT = out_dir("01_archetypes")


def build_profiles(scores: pd.DataFrame, players: pd.DataFrame) -> pd.DataFrame:
    scores = scores.sort_values(["PlayerId", "Pp"], ascending=[True, False])
    counts = scores.groupby("PlayerId").size()
    scores = scores[scores.PlayerId.isin(counts[counts >= MIN_SCORES].index)]
    rows = []
    for pid, g in scores.groupby("PlayerId", sort=False):
        ceiling = float(np.quantile(g.PassRating, 0.95))  # speed the player has demonstrated at all
        rows.append({"PlayerId": pid, **profile(g.head(TOP_N), ceiling)})
    prof = pd.DataFrame(rows).merge(players, left_on="PlayerId", right_on="Id")
    return prof.sort_values("Rank").reset_index(drop=True)


def rule_style(p) -> str:
    """Rough PlayerStyles-like rule (portaBLerebalancer2/Services/PlayerStyles.cs) + 'extreme' guess, for comparison."""
    if p.share_13plus >= 0.5 and p.s_sub95 >= 0.5:  # user definition: 13★+ (often modifier-boosted), mostly < 95%
        return "extreme"
    if p.stars <= 5:
        return "low-star acc"
    if p.stars <= 9:
        return "mid tech acc" if p.s_type_tech >= 0.4 else "mid midspeed acc"
    return "high tech" if p.s_type_tech >= 0.4 else "speed"


def main():
    con = connect()
    lbs, players = load_leaderboards(con), load_players(con)
    scores = load_scores(con).merge(lbs, left_on="LeaderboardId", right_on="Id", suffixes=("", "_lb"))
    scores = scores[scores.ModeName == "Standard"]
    scores = apply_modifier_ratings(scores, pd.read_sql_query("select * from ModifiersRating", con))
    prof = build_profiles(scores, players)
    prof.to_csv(f"{OUT}/profiles.csv", index=False)
    style_cols = [c for c in prof.columns if c.startswith("s_")]
    report = [f"# Exploration 01 — archetypes\n", f"Players with >= {MIN_SCORES} ranked scores: {len(prof):,}\n"]

    # --- 1. discover clusters among top players -------------------------------------------------------
    top = prof[prof.Rank <= TOP_PLAYERS].copy()
    scaler = StandardScaler().fit(top[style_cols])
    X = scaler.transform(top[style_cols])
    report.append(f"\n## 1. Clusters among top {TOP_PLAYERS} players (style features only)\n")
    report.append("| k | GMM BIC | KMeans silhouette |\n|---|---|---|\n")
    best = None
    for k in range(2, 10):
        gmm = GaussianMixture(k, covariance_type="full", n_init=5, random_state=0).fit(X)
        km = KMeans(k, n_init=10, random_state=0).fit(X)
        sil = silhouette_score(X, km.labels_)
        report.append(f"| {k} | {gmm.bic(X):,.0f} | {sil:.3f} |\n")
        if best is None or gmm.bic(X) < best[1]:
            best = (k, gmm.bic(X), gmm)
    k_best, _, gmm = best
    # also force k=5 (the user's archetype count) for interpretation
    for k, model in [(k_best, gmm), (5, GaussianMixture(5, covariance_type="full", n_init=5, random_state=0).fit(X))]:
        top[f"c{k}"] = model.predict(X)
        report.append(f"\n### k = {k} cluster profiles (means; absolute descriptors + key style features)\n")
        desc = top.groupby(f"c{k}").agg(
            n=("PlayerId", "size"), median_rank=("Rank", "median"), stars=("stars", "mean"), pass_=("pass", "mean"),
            tech=("tech", "mean"), acc=("acc", "mean"), acc_resid=("s_acc_resid", "mean"),
            below_ceiling=("s_below_ceiling", "mean"), type_acc=("s_type_acc", "mean"),
            type_tech=("s_type_tech", "mean"), type_speed=("s_type_speed", "mean"),
            pp_acc=("s_pp_acc", "mean"), pp_tech=("s_pp_tech", "mean"), speed_mods=("s_speed_mods", "mean"),
            sub95=("s_sub95", "mean"), share_13plus=("share_13plus", "mean"))
        report.append(desc.round(3).to_markdown() + "\n")
        rules = pd.crosstab(top[f"c{k}"], top.apply(rule_style, axis=1))
        report.append(f"\nCross-tab vs rule-based styles:\n\n{rules.to_markdown()}\n")
    top.to_csv(f"{OUT}/top_players_clusters.csv", index=False)

    # PCA plot
    pca = PCA(2).fit(X)
    P = pca.transform(X)
    fig, ax = plt.subplots(1, 2, figsize=(14, 6))
    ax[0].scatter(P[:, 0], P[:, 1], c=top["c5"], cmap="tab10", s=6)
    ax[0].set_title(f"Top {TOP_PLAYERS}: GMM k=5 (PCA of style features)")
    sc = ax[1].scatter(P[:, 0], P[:, 1], c=top["stars"], cmap="viridis", s=6)
    ax[1].set_title("Same, coloured by pp-weighted stars of top plays")
    fig.colorbar(sc, ax=ax[1])
    fig.savefig(f"{OUT}/top_players_pca.png", dpi=110)
    loadings = pd.DataFrame(pca.components_.T, index=style_cols, columns=["PC1", "PC2"]).round(2)
    report.append(f"\nPCA explained variance: {pca.explained_variance_ratio_.round(3).tolist()}\n\n"
                  f"{loadings.to_markdown()}\n")

    # --- 2. divergence across rank bands ----------------------------------------------------------------
    gmm5 = GaussianMixture(5, covariance_type="full", n_init=5, random_state=0).fit(X)
    bands = [(1, 500), (501, 2000), (2001, 5000), (5001, 10000), (10001, 20000), (20001, 40000), (40001, 10**7)]
    report.append("\n## 2. Divergence by rank band\n")
    report.append("Style spread = mean distance to band centroid in top-player-standardised style space. "
                  "Confidence = mean max posterior under the top-player GMM (k=5). Silhouette = KMeans k=5 within band.\n\n")
    report.append("| ranks | players | median stars | style spread | GMM confidence | within-band silhouette | rule styles |\n"
                  "|---|---|---|---|---|---|---|\n")
    curve = []
    for lo, hi in bands:
        b = prof[(prof.Rank >= lo) & (prof.Rank <= hi)]
        if len(b) < 50:
            continue
        Xb = scaler.transform(b[style_cols])
        spread = float(np.linalg.norm(Xb - Xb.mean(0), axis=1).mean())
        conf = float(gmm5.predict_proba(Xb).max(1).mean())
        sample = Xb if len(Xb) <= 5000 else Xb[np.random.default_rng(0).choice(len(Xb), 5000, replace=False)]
        sil = silhouette_score(sample, KMeans(5, n_init=5, random_state=0).fit_predict(sample))
        styles = b.apply(rule_style, axis=1).value_counts(normalize=True).round(2).to_dict()
        report.append(f"| {lo}-{hi} | {len(b):,} | {b.stars.median():.2f} | {spread:.2f} | {conf:.2f} | {sil:.3f} | {styles} |\n")
        curve.append((lo, hi, spread, conf, sil))
    fig, ax = plt.subplots(figsize=(8, 4))
    labels = [f"{lo}-{hi}" for lo, hi, *_ in curve]
    ax.plot(labels, [c[2] for c in curve], marker="o", label="style spread")
    ax.plot(labels, [c[3] for c in curve], marker="o", label="GMM confidence")
    ax.plot(labels, [c[4] * 5 for c in curve], marker="o", label="silhouette x5")
    ax.legend(); ax.set_xlabel("rank band"); plt.setp(ax.get_xticklabels(), rotation=30)
    fig.tight_layout(); fig.savefig(f"{OUT}/divergence.png", dpi=110)

    open(f"{OUT}/report.md", "w", encoding="utf-8").write("".join(report))
    print("".join(report))


if __name__ == "__main__":
    main()
