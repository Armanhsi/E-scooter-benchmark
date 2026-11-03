# Surface Vibration vs Speed — Formula for the VR Simulator

In the VR simulator the platform vibrates inside a `RumbleZone` with a **fixed sine** (20 Hz, constant strength), whatever the surface or speed.
This folder builds a vibration formula **from our own field recordings** that depends on the **surface** (sidewalk / asphalt) and the **scooter speed**, tests it against the real vibration, and implements it in Unity.

Other papers were read for ideas (road roughness as a function of distance, ISO 8608; data-driven haptic textures; sidewalk joint impacts), but the formula and all its numbers come from our data.

The notebook answers four questions:

1. How does vibration strength change with speed, on each surface?
2. Do the vibration frequencies move with speed, or are they fixed by the scooter?
3. Are there regular hits (sidewalk joints, wheel)?
4. Does a synthetic signal made only from surface + speed look like the real vibration?

---

## Contents

| File | Description |
|---|---|
| `vibration_speed_model.ipynb` | Full analysis with outputs, one commented cell per step |
| `requirements.txt` | Python dependencies |
| `results/` | Figures produced by the notebook |
| `unity/` | Unity scripts that use the formula: `SurfaceVibration.cs` (new), `RumbleZone.cs`, `SigmaMotionController.cs` (replace the old ones) |

## How to run

Written for **Google Colab** (CPU is enough, a few minutes) with the data on Google Drive.
It uses the IMU windows saved by [`escooter-surface-classification-full`](../escooter-surface-classification-full/) (`classification_full/imu_windows.npz` and `imu_windows_meta.csv`), so run that notebook first.
Results are written to `scooter_data/vibration_model/`.

**Speed** comes from GPS: the scooter bag GPS if it has one, otherwise the phone GPS (Sensor Logger).
**Labels** come from the camera; wrong ones can be fixed in two ways:

- cell 3: a click tool that shows a recording 12 s at a time (6 frames) to label it by eye; answers are saved to `label_review.csv` (used for 28 Oct, where the camera points up);
- `label_fix` in cell 2: a list of `(recording, from s, to s, label)`.

---

## What was done

### Step 1 – Windows with speed and checked labels (cells 2–4)

12 s windows of vertical vibration (200 Hz, gravity removed, 0.5–80 Hz) with the camera surface label and the median GPS speed.
Only windows with a steady speed above 1 m/s are used.

- 28 Oct was checked by eye (38 blocks): 57 windows changed label, 27 mixed windows dropped.
- 2 windows of `lab_to_home` were fixed by hand (sidewalk labelled as asphalt).

Used: **328 windows** (sidewalk 123, asphalt 205) from 10 recordings.

| Recording | Windows used | GPS |
|---|---|---|
| field_1014 | 84 | scooter |
| lab_to_home | 69 | scooter |
| field_1028 | 66 | phone |
| field_recording | 54 | scooter |
| gps_outdoor | 28 | scooter |
| field_1026 | 16 | phone |
| field_1016 / 1019 / 1020 / 1021 | 4 / 3 / 3 / 1 | phone |

`scooter_15min` has no GPS and is not used.

### Step 2 – Strength vs speed (cells 5–6)

Fit per surface: `RMS = K · (v / 4 m/s) ^ alpha`. Three models were compared by leave-one-recording-out error: no speed, one alpha for both surfaces, one alpha per surface.

### Step 3 – Frequencies vs speed (cell 7)

The average spectrum shape in speed groups, plotted against frequency `f` and against `f / v` (cycles per metre).
Curves that overlap in `f` mean the frequencies are set by the scooter and rider; overlap in `f / v` means they come from bumps fixed on the ground.

### Step 4 – Regular hits and the wheel (cells 8, 11)

Period of the hits in each window and the spacing on the ground (`L = v · T`), and a search for any vibration that repeats every `C` metres.

### Step 5 – The formula and its test (cells 12–15)

Hits are cut out to get the base vibration and its spectrum shape; the hit shape and sizes are measured separately.
Then, for every real window, a synthetic signal is made from its surface and speed only and compared with the real one.

---

## Notebook cells

| Cell | Step |
|---|---|
| 1–2 | Libraries, paths, settings, `label_fix` |
| 3 | Label review by eye (click tool) |
| 4 | Apply label fixes, GPS speed per window |
| 5 | RMS, band RMS (0.5–5, 5–15, 15–25, 25–40, 40–80 Hz), peak frequency, hits per window |
| 6 | Strength vs speed per surface, model comparison |
| 7 | Spectrum vs speed (`f` and `f / v`) |
| 8 | Hit period and spacing on the ground |
| 9 | Save per-surface numbers (`vibration_model_params.json`) |
| 10 | Unusual windows (> 2× the fit) with their camera frame |
| 11 | Wheel: repeat distance and amplitude |
| 12 | Base vibration and hits |
| 13 | Formula test: synthetic vs real |
| 14–15 | Save the formula (`vibration_formula.json`) and the Unity file (`vibration_formula_unity.json`) |

---

## Results

### The formula

```
a(t) = K_surface · roughness · (v / 4)^0.43 · noise(t)        base vibration (measured spectrum, peaks near 5 and 18 Hz)
     + 0.21 · sin(2π · v · t / 0.69)                          one vibration per wheel turn
     + hits at random times, rate = hits_per_m · v            sharp bumps, measured shape and sizes
```

| | K (m/s², at 4 m/s) | Hits per metre | Median hit size | Speed range in the data |
|---|---|---|---|---|
| Sidewalk | 2.12 | 0.33 | 13.9 m/s² | 2.5–5.8 m/s |
| Asphalt | 1.62 | 0.08 | 12.4 m/s² | 3.3–7.6 m/s |

`v` = scooter speed; no vibration when standing still. `roughness` is a per-zone factor in Unity (1 = average).

### Findings

- **Speed:** vibration grows with speed, but less than proportionally: about `v^0.4–0.5` (sidewalk 0.54, asphalt 0.43 alone; 0.43 with one alpha for both). One alpha for both surfaces fits unseen recordings best (error 0.376, vs 0.401 without speed and 0.396 with one alpha per surface).
- **Surface:** at the same speed, sidewalk vibrates about **1.3×** as strongly as asphalt and has about **4×** as many sharp hits per metre.
- **Frequencies do not move with speed** (spread 0.19–0.24 against `f`, 0.48–0.52 against `f / v`). They are set by the scooter and rider: peaks near **5 Hz** and **18 Hz**, both surfaces have almost the same spectrum shape. So the surface changes mainly the strength and the hits, not the "sound" of the vibration.
- `v^0.5` with a fixed spectrum is what a road with ISO-8608-like roughness (waviness 2) through a scooter with fixed resonances would give, so the two findings agree.
- **Wheel:** a vibration repeats every **0.69 m** on both surfaces = one turn of a 22 cm (8.5 inch) wheel. It is small (≈ 0.2 m/s²).
- **Formula test:** the synthetic RMS is 1.02× the real one (median; middle 50 % of windows 0.73–1.31), and the average spectra overlap.
- About half of the energy is above 25 Hz, which a 50 Hz physics step in Unity cannot send; the new Unity code sends at 200 Hz from its own thread.

### Figures

| File | Content |
|---|---|
| `results/rms_vs_speed.png` | Vertical and horizontal RMS vs GPS speed with the fits |
| `results/alpha_per_band.png` | How fast each frequency band grows with speed |
| `results/spectrum_vs_speed.png` | Spectrum shape in speed groups, against `f` and `f / v` |
| `results/hits_and_spacing.png` | Periodicity, hit spacing on the ground, hits per second vs speed |
| `results/unusual_windows.png` | Windows with much more vibration than the fit, with camera frames |
| `results/wheel_repeat_distance.png` | Search for a vibration repeating every `C` metres |
| `results/base_shape_and_hit.png` | Base spectrum shape and average hit shape |
| `results/formula_vs_real.png` | Synthetic vs real: time series and average spectrum |

### Limitations

- Speed explains only part of the differences between windows (R² 0.1–0.4); most of the rest comes from the road patch itself. That is why each zone in Unity has a `roughness` factor (real patches were mostly 0.7–1.3, rough spots up to about 2).
- GPS speed is a 12 s median; about 90 % of the windows come from 5 recordings (12–14 and 28 Oct).
- The vibration was measured on the scooter (camera IMU), not on the motion platform; how much of it the Sigma platform can reproduce is not known yet.
- Labels come from the camera (checked by eye only for 28 Oct and the unusual windows).

---

## Unity

| File | What it does |
|---|---|
| `SurfaceVibration.cs` | New. Reads `vibration_formula_unity.json` and makes the vibration sample by sample from surface, roughness and speed |
| `RumbleZone.cs` | Replaces the old one. Each zone has `Surface` (Sidewalk / Asphalt) and `Roughness`; `Use Formula` off gives the old sine |
| `SigmaMotionController.cs` | Replaces the old one. Adds the vibration to heave and sends packets at a steady 200 Hz from its own thread |

Setup: replace the two scripts (keep their `.meta` files), add `SurfaceVibration.cs`, put `vibration_formula_unity.json` (from `scooter_data/vibration_model/` on Drive) in `Assets` and drag it onto **Formula Json** of `SigmaMotionController`.
Without the json, built-in values from this analysis are used.
Tuning on the platform: `vibrationGain` (whole vibration), `hitGain` (hits only), `maxVibrationFrequencyHz`. Start with a low `masterIntensity`.
The scripts have not been compiled in the project yet.
