# WBC-MTL-SEGCLS 🔬

**Multi-task White Blood Cell segmentation and classification using U-Net ResNet34**
Simultaneous pixel-wise segmentation and cell-type classification in a single forward pass.

---

# Overview

<p align="center">
  <img src="assets/gradcam_examples.png" width="500">
</p>
<p align="center">
  <em>Grad-CAM heatmaps showing regions that most strongly influence the model's classification predictions.</em>
</p>

WBC-MTL-SEGCLS is a multi-task deep learning framework for automated White Blood Cell analysis from microscopy images. The project jointly performs two tasks in a single forward pass:

- **Segmentation** — pixel-wise labeling of nucleus, cytoplasm, and background
- **Classification** — cell-type identification across four WBC categories: Lymphocyte, Monocyte, Neutrophil, and Eosinophil

The pipeline is implemented in PyTorch and leverages transfer learning with a pretrained ResNet-34 backbone integrated into a U-Net decoder architecture. Unlike single-task approaches, WBC-MTL-SEGCLS shares a common encoder between both tasks, enabling efficient joint learning and improved feature representations.

The framework includes preprocessing, augmentation with minority-class handling, multi-task training, evaluation, Grad-CAM explainability, and checkpoint saving for reproducible and deployment-ready workflows.

---

# Features

## WBC Image Preprocessing

Input microscopy images are resized and normalized using ImageNet statistics for compatibility with the pretrained ResNet-34 backbone.

The preprocessing pipeline includes:

- Image resizing to 256×256
- RGB conversion
- Grayscale mask parsing and class mapping
- Tensor normalization via Albumentations

These preprocessing operations ensure stable training behavior and consistent input representation across all dataset splits.

---

## Data Augmentation

To improve generalization and address class imbalance, two augmentation pipelines are applied during training.

**Standard augmentation** (applied to all training samples):
- Horizontal flip
- Random 90° rotation
- Shift, scale, and rotate
- Random brightness and contrast adjustment

**Minority-class augmentation** (applied to the two least-represented classes):
- Stronger horizontal flip probability
- Higher rotation and affine intensity
- Gaussian blur

These augmentations help the model learn robust morphological features under varying staining conditions and improve performance on underrepresented cell types.

---

## Multi-Task Architecture — UNetResNet34MultiTask

WBC-MTL-SEGCLS uses a pretrained ResNet-34 backbone as a shared encoder, combined with a 4-stage U-Net decoder for segmentation and a classification head operating on the bottleneck features.

```
Input image (256×256 RGB)
        │
   ResNet-34 Encoder (ImageNet pre-trained)
   enc0 → enc1 → enc2 → enc3 → enc4
        │
   Bottleneck (1024-ch Conv layers)
   ┌────┴──────────────────────────────────┐
   │  U-Net Decoder (4 skip connections)  │   GlobalAvgPool → FC
   │  dec4 → dec3 → dec2 → dec1           │
   └───────────────────────────────────────┘
        │                                       │
  Segmentation head (3 classes)     Classification head (4 classes)
  nucleus / cytoplasm / background  Lymphocyte / Monocyte / Neutrophil / Eosinophil
```

The segmentation head produces full-resolution pixel-wise predictions. The classification head pools the bottleneck representation and passes it through a fully connected layer. Both heads are trained jointly with a combined loss.

---

## Multi-Task Loss

Both tasks are optimized simultaneously using a weighted combination of cross-entropy losses:

```
L = L_seg + 0.7 × L_cls
```

The segmentation loss receives full weight while the classification loss is scaled by 0.7, balancing the contribution of each task during training.

---

## Class Imbalance Handling

To address unequal class distributions in the training set, the framework applies two complementary strategies:

- **WeightedRandomSampler** — each training sample is weighted inversely proportional to its class frequency, ensuring all classes are seen equally during training
- **Minority-class augmentation** — the two least-represented classes receive a heavier augmentation pipeline with stronger flips, rotations, and blur

These strategies together reduce bias toward majority classes and improve recall on underrepresented WBC types.

---

## Evaluation Metrics

The framework provides comprehensive evaluation metrics to assess both tasks.

**Classification metrics:**
- Accuracy
- Precision (weighted)
- Recall (weighted)
- F1-score (weighted)
- Full per-class classification report

**Segmentation metric:**
- Mean Dice score over foreground classes (nucleus + cytoplasm)

These metrics provide both global and class-wise performance analysis, enabling reliable evaluation of diagnostic capability and model generalization.

---

## Visualization Utilities

WBC-MTL-SEGCLS includes visualization modules for qualitative model evaluation and explainability analysis.

### Segmentation Visualization

Side-by-side comparison of:
- Original microscopy image
- Ground-truth segmentation mask
- Predicted segmentation mask

Color mapping used:
| Color | Class |
|-------|-------|
| Black | Background |
| Green | Nucleus |
| Red | Cytoplasm |

### Classification Prediction Visualization

The framework visualizes sampled test predictions split into:
- Correctly classified samples (green title)
- Incorrectly classified samples (red title)

### Grad-CAM Explainability

Grad-CAM heatmaps are generated using forward and backward hooks on the bottleneck layer (`model.center`). The heatmap is overlaid on the original image to show which regions most strongly influence the classification decision.

This enables interpretable deep learning analysis and provides insight into learned morphological features, improving transparency in the cell-type identification process.

---

## Model Checkpoint Support

The project supports saving and loading trained model checkpoints for:
- Model reuse
- Inference
- Fine-tuning
- Continued training
- Reproducibility

Trained checkpoints can be found inside:

```text
models/checkpoints/
```

The checkpoint file stores:
- Trained model weights
- Optimizer state
- Training epoch
- Model configuration (`n_classes_seg`, `n_classes_cls`)
- Class names
- Label encoder classes
- Final evaluation metrics

This allows the model to be reused without retraining from scratch and enables reproducible experimentation across environments.

---

# Dataset

The framework uses the [segmentation_WBC](https://github.com/zxaoyou/segmentation_WBC) dataset, which contains two subsets of microscopy images with paired grayscale segmentation masks and CSV class label files.

```text
segmentation_WBC/
│
├── Dataset 1/
│   ├── 001.bmp        ← microscopy image
│   ├── 001.png        ← segmentation mask
│   └── ...
│
├── Dataset 2/
│   ├── 001.bmp
│   ├── 001.png
│   └── ...
│
├── Class Labels of Dataset 1.csv
└── Class Labels of Dataset 2.csv
```

Mask pixel value mapping:

| Pixel value | Class |
|-------------|-------|
| 0 | Background |
| 128 | Nucleus |
| 255 | Cytoplasm |

Cell-type class labels:

| Label | Class name |
|-------|------------|
| 1 | Lymphocyte |
| 2 | Monocyte |
| 3 | Neutrophil |
| 4 | Eosinophil |

> Dataset images and CSVs are **not** included in this repository. Clone or download them from the link above and set `BASE_PATH` in `configs/config.py` to your local dataset root.

Classes with fewer than 20 samples are automatically filtered out. Samples with missing image or mask files are dropped before training.

---

# Dataset Split

The dataset is automatically divided into:

- **80%** Training
- **20%** Testing

Stratified splitting is used to preserve class balance across both subsets and ensure fair evaluation.

---

# Training Pipeline

The complete training workflow includes:

- Dataset loading and CSV merging
- Class filtering and label encoding
- Stratified train/test split
- WeightedRandomSampler construction
- Dual augmentation pipeline (standard + minority)
- Multi-task U-Net ResNet34 initialization with ImageNet weights
- Joint segmentation and classification training
- Per-epoch loss and accuracy reporting
- Final evaluation on test set
- Segmentation visualization
- Classification prediction visualization
- Grad-CAM explainability
- Checkpoint saving

The modular pipeline design allows easy experimentation and extension to additional WBC categories or alternative encoder architectures.

---

# Performance

The final model achieved the following performance on the test set:

### Classification

```text
Accuracy : 0.8481
Precision: 0.8816
Recall   : 0.8481
F1-score : 0.8554
```

### Segmentation

```text
Dice (mean foreground): 0.9171
```

### Per-class Classification Report

```text
              precision    recall  f1-score   support

  Lymphocyte       1.00      0.88      0.94        41
    Monocyte       0.87      0.72      0.79        18
  Neutrophil       0.60      0.92      0.73        13
  Eosinophil       0.75      0.86      0.80         7

    accuracy                           0.85        79
   macro avg       0.80      0.85      0.81        79
weighted avg       0.88      0.85      0.86        79
```

The model demonstrates strong segmentation performance (Dice 0.917) and reliable classification across all four WBC categories, with near-perfect precision on Lymphocytes.

---

# Requirements

The following libraries are required:

```text
Python >= 3.9
torch >= 2.2.0
torchvision >= 0.17.0
numpy >= 1.24
pandas >= 2.0
scikit-learn >= 1.3
albumentations >= 1.4
Pillow >= 10.0
opencv-python >= 4.9
matplotlib >= 3.8
```

Install dependencies via:

```bash
pip install -r requirements.txt
```

---

# Quick Start

```bash
# 1. Clone the repository
git clone https://github.com/<you>/WBC-MTL-SEGCLS.git
cd WBC-MTL-SEGCLS

# 2. Install dependencies
pip install -r requirements.txt

# 3. Download the dataset
#    https://github.com/zxaoyou/segmentation_WBC

# 4. Set your dataset path
#    Open configs/config.py and update BASE_PATH

# 5. Train and evaluate
python main.py
```

---

# Project Structure

```text
WBC-MTL-SEGCLS/
│
├── models/
│   ├── model.py              # UNetResNet34MultiTask
│   ├── gradcam.py            # GradCAM class + show_gradcam visualization
│   └── checkpoints/
│       └── README.md         # How to load a saved checkpoint
│
├── data/
│   ├── dataset.py            # WBCDataset (image + mask + label)
│   ├── transforms.py         # Albumentations pipelines (train / minority / test)
│   ├── prepare_data.py       # CSV merge, filtering, label encoding, split
│   └── label_utils.py        # normalize_id, convert_mask
│
├── src/
│   ├── train.py              # Training loop (seg + cls joint loss)
│   ├── eval.py               # Metrics + classification report + confusion matrix
│   ├── metrics.py            # dice_score
│   ├── losses.py             # CrossEntropyLoss (extensible to Dice / Focal)
│   ├── sampler.py            # WeightedRandomSampler builder
│   ├── visualize.py          # show_results, show_predictions
│   └── utils.py              # print_distribution, build_idx_to_label
│
├── configs/
│   └── config.py             # All paths, hyperparameters, class names, colours
│
├── assets/                   # Output figures (not tracked by Git)
├── main.py                   # Entry point (train + eval + visualize + save)
├── requirements.txt
├── .gitignore
├── LICENSE
└── README.md
```

---

# Checkpoints

The trained model checkpoint is saved to:

```text
models/checkpoints/wbc_multitask_checkpoint.ckpt
```

Loading the checkpoint:

```python
import torch
from models.model import UNetResNet34MultiTask

ckpt  = torch.load('models/checkpoints/wbc_multitask_checkpoint.ckpt', map_location='cpu')
model = UNetResNet34MultiTask(**ckpt['model_config'])
model.load_state_dict(ckpt['model_state_dict'])
model.eval()
```

---

# Visualization

To improve interpretability and qualitative evaluation, the project includes multiple visualization utilities.

### Segmentation Samples

<p align="center">
  <img src="assets/segmentation_samples.png" width="600">
</p>

_Side-by-side comparison of original image, ground-truth mask, and predicted mask for test samples._

---

### Classification Predictions

<p align="center">
  <img src="assets/prediction_results.png" width="600">
</p>

_Correctly classified samples (green) and incorrectly classified samples (red) drawn from the test set._

---

### Confusion Matrix

<p align="center">
  <img src="assets/confusion_matrix.png" width="400">
</p>

_Per-class classification performance across all four WBC categories._

---

### Grad-CAM Explainability

<p align="center">
  <img src="assets/gradcam_examples.png" width="500">
</p>

_Grad-CAM heatmaps overlaid on test images showing which morphological regions drive the model's classification decisions._

---

# Notes

- The project supports CUDA and CPU execution (device is auto-detected).
- Grad-CAM is implemented using forward and backward hooks on `model.center` (the bottleneck layer).
- The framework is modular and can be extended to additional WBC categories or alternative encoder architectures.
- Dataset images and CSV files are not tracked by Git (see `.gitignore`).
- Model checkpoints (`.ckpt`) are not tracked by Git.

---

# License

MIT
