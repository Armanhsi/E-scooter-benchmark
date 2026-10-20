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

---

## Outputs (`results/`)

| File | Content |
|---|---|
| `raw_full_*.png` | Raw acceleration, full recording |
| `raw_zoom_60_70s_*.png`, `raw_zoom_100_110s_*.png` | Raw acceleration, 10-s zooms |
| `gravity_removal_comparison_*.png` | Mean-remove vs. low-pass gravity removal (100–110 s) |
| `smoothing_effect_*.png` | `sqrt(ay² + az²)` before and after the 1-s moving average |
| `magnitude_methods_*.png` | The three vibration magnitudes, for both gravity-removal methods |
| `normalization_*.png` | Chosen signal before and after global normalization |
| `threshold_effect_*.png`, `threshold_effect_zoom_Sidewalk.png` | Effect of the smoothstep threshold |
| `unity_vibration_output_both.png` | Final signal for both surfaces on the shared scale |
| `unity_vibration_output_*.csv` | `time_sec`, `vibration_norm`, `vibration_threshold` per surface |

---

## Results

| | Sidewalk | Asphalt |
|---|---|---|
| Samples / duration | 110,881 / 549.4 s | 119,128 / 593.1 s |
| Sampling rate (median) | ~200.7 Hz | ~201.0 Hz |
| Largest timing gap | 0.38 s | 1.32 s |
| Mean absolute difference, mean-remove vs. low-pass, full recording (ax / ay / az, m/s²) | 0.52 / 2.78 / 1.74 | 0.75 / 0.23 / 0.46 |
| Normalized range (global min–max) | 0.044 – 0.946 | 0.000 – 1.000 |
| Samples near zero after threshold (< 0.01) | 55.8 % | 79.1 % |

Global normalization bounds (`sqrt(ay² + az²)`, mean-remove, 1-s moving average): min 0.114, max 27.30 m/s².

---

## Observations

- Mean-removal and low-pass give clearly different results over the full recordings, especially on the sidewalk: the sensor orientation changed during the ride, so a single mean is not a good gravity estimate. The final pipeline therefore uses the low-pass method.
- The large share of near-zero samples, especially on asphalt, comes mostly from stationary periods in the recordings, not from the surface itself; the final pipeline removes stationary intervals before comparing surfaces.
- The final pipeline also adds band-limiting (0.5–80 Hz), removal of stationary intervals, spectral analysis and statistics; see `escooter-vibration-validation`.
