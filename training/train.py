"""
Homelab YOLO11n-OBB Training Script
Auto-detects CUDA/MPS/CPU and trains PhotoCropper model with full GPU acceleration.
"""

import os
import shutil
import torch
from ultralytics import YOLO

def main():
    print("=" * 60)
    print(" PhotoCropper Model Training (Homelab GPU Edition)")
    print("=" * 60)

    device = "0" if torch.cuda.is_available() else ("mps" if torch.backends.mps.is_available() else "cpu")
    print(f"[*] Detected PyTorch device: {device.upper()}")
    if torch.cuda.is_available():
        print(f"[*] GPU Model: {torch.cuda.get_device_name(0)}")

    dataset_yaml = os.path.abspath("dataset/data.yaml")
    if not os.path.exists(dataset_yaml):
        # Fallback check
        dataset_yaml = os.path.abspath("../PhotoCropper.DatasetGenerator/bin/Debug/net10.0/dataset/data.yaml")

    if not os.path.exists(dataset_yaml):
        print(f"[!] Error: Could not find data.yaml at {dataset_yaml}")
        return

    print(f"[*] Using dataset: {dataset_yaml}")
    model = YOLO("yolo11n-obb.pt")

    print("[*] Starting training (50 epochs, imgsz=640)...")
    results = model.train(
        data=dataset_yaml,
        epochs=50,
        imgsz=640,
        batch=32 if torch.cuda.is_available() else 16,
        workers=8 if torch.cuda.is_available() else 4,
        device=device,
        project="runs/homelab_train",
        name="photo_cropper_ai"
    )

    best_pt = os.path.join(model.trainer.save_dir, "weights", "best.pt")
    print(f"[*] Best weights saved at: {best_pt}")

    print("[*] Exporting best weights to ONNX (imgsz=640, opset=17)...")
    best_model = YOLO(best_pt)
    onnx_file = best_model.export(format="onnx", imgsz=640, opset=17, simplify=True)

    target_onnx = os.path.abspath("photo_detector.onnx")
    shutil.copyfile(onnx_file, target_onnx)
    print(f"\n[+] SUCCESS! Exported model saved as: {target_onnx}")
    print("[+] Copy 'photo_detector.onnx' into PhotoCropper/Models/photo_detector.onnx on your app!")

if __name__ == "__main__":
    main()
