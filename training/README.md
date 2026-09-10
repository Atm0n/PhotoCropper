# PhotoCropper AI Model Training (Homelab)

## 1. Quick Start (Linux / Server with GPU / Docker)

### Option A: Direct Python / Conda / Venv
```bash
# 1. Install dependencies
pip install -r requirements.txt

# 2. Run training (auto-detects CUDA GPU)
python train.py
```

### Option B: Docker (NVIDIA GPU)
```bash
docker run --gpus all -it --rm -v $(pwd):/workspace -w /workspace ultralytics/ultralytics:latest python train.py
```

## 2. Output
When training completes (usually 2-5 minutes on a GPU):
- `photo_detector.onnx` will be generated in this folder.
- Simply copy `photo_detector.onnx` into `PhotoCropper/Models/photo_detector.onnx` on your development PC.
