# E-Scooter IMU Dataset — Exploration

First look at the two field recordings (sidewalk and asphalt) and a comparison of candidate methods for turning raw acceleration into a vibration signal for Unity.

**This is exploratory work.** The final, validated processing pipeline is in [`escooter-vibration-validation`](../escooter-vibration-validation/).

---

## Contents

| File | Description |
|---|---|
| `dataset_exploration.ipynb` | Data check and method comparison, one commented cell per step |
| `results/` | Figures and the vibration signals saved for Unity (created when the notebook runs) |

## How to run

```bash
pip install pandas numpy matplotlib jupyter
jupyter notebook dataset_exploration.ipynb
```

The notebook reads the CSV files from `data_dir` (cell 1), currently `~/Desktop/scooter_pro`. Change it to the folder where the CSV files are on your machine.

---

## What the notebook does

| Section | Step |
|---|---|
| 1–3 | Load both recordings, summary statistics, sampling rate (~200 Hz) and largest timing gaps |
| 4–5 | Raw acceleration plots: full recordings and 10-s zooms |
| 6–8 | Two ways to remove gravity (subtract the recording mean vs. a 0.5 Hz first-order low-pass) and their difference on a 10-s window and on the full recordings |
| 9–11 | Three candidate vibration magnitudes (`|az|`, `sqrt(ay² + az²)`, `sqrt(ax² + ay² + az²)`) for both gravity-removal methods, smoothed with a 1-s moving average |
| 12 | Global min–max normalization over both recordings (keeps the intensity difference between surfaces) |
| 13–14 | Smoothstep threshold (0.15 → 0.3) to suppress low-level noise |
| 15 | Save `time_sec`, `vibration_norm`, `vibration_threshold` per surface to `results/` (all figures are saved there too) |

## Observations

- Mean-removal and low-pass give clearly different results over the full recordings, especially on the sidewalk: the sensor orientation changed during the ride, so a single mean is not a good gravity estimate. The final pipeline therefore uses the low-pass method.
- The final pipeline also adds band-limiting (0.5–80 Hz), removal of stationary intervals, spectral analysis and statistics; see `escooter-vibration-validation`.
