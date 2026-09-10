"""
Export YOLO11 Nano Oriented Bounding Box (OBB) model to ONNX format.
This script downloads the pretrained PyTorch weights and exports them to an ONNX model
optimized for 640x640 input resolution and places it into the PhotoCropper Models directory.
"""

import os
import shutil
from ultralytics import YOLO

def export_model():
    model_name = "yolo11n-obb.pt"
    onnx_target_dir = os.path.join(os.path.dirname(__file__), "..", "PhotoCropper", "Models")
    onnx_target_path = os.path.join(onnx_target_dir, "photo_detector.onnx")

    print(f"[*] Loading model: {model_name}...")
    model = YOLO(model_name)

    print("[*] Exporting model to ONNX (imgsz=640, opset=17)...")
    exported_file = model.export(format="onnx", imgsz=640, opset=17, simplify=True)

    print(f"[*] Exported to: {exported_file}")
    os.makedirs(onnx_target_dir, exist_ok=True)
    
    print(f"[*] Copying to: {onnx_target_path}")
    shutil.copyfile(exported_file, onnx_target_path)
    print("[+] Model ready in PhotoCropper/Models/photo_detector.onnx!")

if __name__ == "__main__":
    export_model()
