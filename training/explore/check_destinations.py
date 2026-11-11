"""Quick check of profiles.destination() against the user's example players and the population.
Needs data/explore/03_thresholds/current_profiles.csv (run 03_thresholds.py after changing profile features)."""
import pandas as pd

from common import anchors, out_dir
from profiles import DESTINATIONS, UNSPECIALISED, destination

# User-provided anchors (docs/DECISIONS.md, D-018..D-021), local only: data/anchors.json
EXAMPLES = anchors()
ORDER = [*DESTINATIONS, UNSPECIALISED]


def main():
    src = out_dir("03_thresholds")
    d = pd.read_csv(f"{src}/current_profiles.csv", dtype={"PlayerId": str})
    d["dest"] = d.apply(destination, axis=1)
    e = d[d.PlayerId.isin(EXAMPLES)]
    wrong = e[e.dest != e.PlayerId.map(EXAMPLES)]
    print(f"examples ok: {len(e) - len(wrong)} / {len(EXAMPLES)}")
    if len(wrong):
        print(wrong.assign(expected=wrong.PlayerId.map(EXAMPLES))[["PlayerId", "Rank", "expected", "dest", "pass_med",
                                                                  "tech_med", "stars_med", "s_acc_resid"]].round(3).to_markdown(index=False))
    rows = []
    for lo, hi in [(1, 500), (501, 2000), (2001, 5000), (5001, 10000), (10001, 20000), (20001, 50000)]:
        b = d[(d.Rank >= lo) & (d.Rank <= hi)]
        rows.append({"ranks": f"{lo}-{hi}", "n": len(b),
                     **b.dest.value_counts(normalize=True).reindex(ORDER).fillna(0).round(3).to_dict()})
    print(pd.DataFrame(rows).to_markdown(index=False))
    print(d.groupby("dest").agg(n=("PlayerId", "size"), rank=("Rank", "median"), stars=("stars_med", "median"),
                                pass_med=("pass_med", "median"), tech_med=("tech_med", "median"), acc=("acc", "median"),
                                resid=("s_acc_resid", "median"), sub95=("s_sub95", "median")).reindex(ORDER).round(3).to_markdown())
    top = d[d.Rank <= 300].sort_values("Rank").groupby("dest").head(4)
    print(top.sort_values(["dest", "Rank"])[["dest", "PlayerId", "Rank", "stars_med", "pass_med", "tech_med", "acc",
                                             "s_sub95"]].round(3).to_markdown(index=False))
    d.to_csv(f"{src}/destinations.csv", index=False)


if __name__ == "__main__":
    main()
