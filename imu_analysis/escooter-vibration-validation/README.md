# Quantitative Validation of Field Vibration

Signal-processing and validation pipeline for the VR e-scooter simulator with field-derived surface vibration.
It implements **Phase 2 (signal processing)** and the field part of **Phase 4.5 (surface separability)** of the *Quantitative Validation* plan.

The notebook answers two questions:

1. What does real e-scooter vibration look like on sidewalk and asphalt, and how do the two surfaces differ?
2. How much of that real vibration is kept in the signal currently sent to Unity?

---

## Contents

| File | Description |
|---|---|
| `quantitative_validation.ipynb` | Full pipeline, one commented cell per step |
| `requirements.txt` | Python dependencies |
| `results/` | Figures and tables produced by the notebook |

## How to run

```bash
pip install -r requirements.txt
jupyter notebook quantitative_validation.ipynb
```

Run the notebook from this folder (*Restart & Run All*). Tested with Python 3.11.

---

## Input data

The notebook reads the recording CSV files from `data_dir` (cell 1), currently `~/Desktop/scooter_pro`. Change it to the folder where the CSV files are on your machine.

Each recording is one CSV with at least these columns:

| Column | Unit | Description |
|---|---|---|
| `time_sec` | s | elapsed time since the start of the recording |
| `ax`, `ay`, `az` | m/s² | linear acceleration from the IMU (Intel RealSense D435i) |
| `speed_mps` *(optional)* | m/s | scooter speed; if present, distance and events per 100 m are added |

Current recordings: `imu_accel_sidewalk_cafe.csv` (sidewalk) and `imu_accel_cafe_asphalt.csv` (asphalt), ~200 Hz.

---

## Usage with new data

Only **cell 1** needs to change:

```python
data_dir = Path.home() / "Desktop" / "scooter_pro"         # folder with the CSV files
files = {"F001": "F001.csv", "F002": "F002.csv"}          # field runs
surface = {"F001": "Sidewalk", "F002": "Asphalt"}          # surface of each run
calib_files = {"F001": "calib_F001.csv"}                   # optional, Phase 0.2
```

Then *Run All*.

All tunable parameters (filter cut-offs, bands, thresholds, window lengths) are in **cell 2**.

---

## Pipeline

| Section | Step | Plan phase |
|---|---|---|
| 3 | Load data, check sampling rate, timing gaps, missing values, mean gravity | — |
| 4 | Rotation to the scooter frame from a stationary calibration recording | 0.2 |
| 5 | Split at timing gaps, resample to 200 Hz, remove gravity (0.3 Hz low-pass), band-limit 0.5–80 Hz (zero-phase), vibration magnitude `v = sqrt(ay² + az²)`, Kalman envelope (display only) | 2.1–2.4 |
| 6 | Motion mask: remove stationary intervals (automatic threshold on 1-s RMS) | — |
| 7 | Welch PSD, peak frequency, spectral centroid, band RMS (0.5–5, 5–20, 20–80 Hz) | 2.5 |
| 8 | Event detection (sharp hits) | 2.5 |
| 9 | Phase 2 output table (one row per run) | 2 |
| 10 | PSD and time-series figures | — |
| 11–12 | Surface separability in the field: Cohen's d, Welch t-test, Mann–Whitney U | 4.5 |
| 13–14 | Spectral content of the current Unity command vs. real vibration; Kalman frequency response | — |

### Implementation notes

- **Spectral features are computed before Kalman smoothing.** With `q = 1e-4`, `r = 1e-2` the Kalman filter is equivalent to a first-order low-pass at ≈ 3.2 Hz and would remove the 5–20 and 20–80 Hz bands.
- **PSD is computed on the axes (`P_y + P_z`), not on the magnitude `v`.** `v` is rectified (always ≥ 0), which adds artificial low-frequency and harmonic energy to its spectrum.
- **Recordings are split at timing gaps** (> 50 ms) and each piece is resampled to a uniform grid before filtering, instead of interpolating across gaps.
- **Gravity filter coefficient:** `α = 1 / (1 + 2π·fc/fs)` ≈ 0.9907 for `fs = 200 Hz` (the plan's 0.9925 assumes 250 Hz).

---

## Outputs (`results/`)

| File | Content |
|---|---|
| `phase2_output.csv` | Phase 2 table: moving time, `a_RMS`, `f_peak`, `f_c`, band RMS, events |
| `field_window_metrics.csv` | Window-level metrics used for the statistics |
| `field_separability.csv` | Cohen's d, t-test and Mann–Whitney results |
| `field_vs_command_band_fractions.csv` | Band power fractions: real vibration vs. Unity command |
| `*.png` | All figures |

---

## Preliminary results (current recordings)

| | Sidewalk | Asphalt |
|---|---|---|
| Moving time | 325.7 s (59 %) | 177.2 s (31 %) |
| `a_RMS` (m/s²) | 8.91 | 8.89 |
| Peak frequency | 30.5 Hz | 31.5 Hz |
| Events per second (provisional threshold) | 0.70 | 0.36 |

- **While moving, vibration intensity is nearly identical on both surfaces** (Cohen's d ≈ 0.06 for `a_RMS`, 0.04–0.06 for the mid and high bands; 0.31 for the low band).
- **The main difference is the rate of sharp events**, about twice as high on the sidewalk.
- **About 70 % of the asphalt recording is stationary**, which explains most of the apparent intensity difference when stationary periods are not removed.
- **About 76 % of real vibration power is above 20 Hz, while about 86 % of the current Unity command's power is below 5 Hz.**

### Limitations

One recording per surface, unmatched speeds, and a handlebar-mounted sensor (steering rotation and handlebar resonance enter the signal). Window-level p-values are optimistic because adjacent windows are correlated. Event thresholds are provisional until tuned on a known-defect segment. These results should be treated as preliminary; the planned deck-mounted, speed-controlled recordings address these limitations.
