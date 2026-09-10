#!/usr/bin/env bash
set -e

echo "================================================="
echo " Starting PhotoCropper AI Model Training..."
echo "================================================="

# Create virtual environment if not existing
if [ ! -f "venv/bin/activate" ] && [ ! -f "venv/Scripts/activate" ]; then
    echo "[*] Creating Python virtual environment (venv)..."
    python3 -m venv venv || python -m venv venv || echo "[!] Notice: venv module not installed (sudo apt install python3-venv), running directly with python3"
fi

if [ -f "venv/bin/activate" ]; then
    echo "[*] Activating virtual environment (Linux/Mac)..."
    source venv/bin/activate
elif [ -f "venv/Scripts/activate" ]; then
    echo "[*] Activating virtual environment (Windows/Git Bash)..."
    source venv/Scripts/activate
fi

echo "[*] Installing/updating requirements..."
python3 -m pip install --upgrade pip || python -m pip install --upgrade pip
python3 -m pip install -r requirements.txt || pip install -r requirements.txt

echo "[*] Setting environment optimizations for AMD Ryzen multi-core..."
export OMP_NUM_THREADS=$(nproc 2>/dev/null || echo 8)
export MKL_NUM_THREADS=$(nproc 2>/dev/null || echo 8)

echo "[*] Starting training..."
python3 train.py || python train.py

echo "================================================="
echo "[+] Training complete! ONNX model exported to: photo_detector.onnx"
echo "================================================="
