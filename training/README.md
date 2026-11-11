# Training (Python)

Exploration and model fitting (D-013). Inputs: feature tables written by `SkillProgression.Tools extract`
into `data/` (gitignored). Output: versioned `model.json` consumed by `SkillProgression.Core`.

Rule (D-007): raw attempts / per-player records never leave `data/`. Only aggregates and fitted parameters
may be written to published model files.

Setup: `python -m venv .venv` then `.venv/Scripts/pip install -r requirements.txt` (to be added).
