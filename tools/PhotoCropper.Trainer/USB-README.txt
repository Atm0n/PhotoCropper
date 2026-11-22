PhotoCropper trainer
====================

Makes practice album pages and trains PhotoCropper's photo-detection model on them.
Nothing to install: only an NVIDIA driver, version 570 or newer (https://www.nvidia.com/drivers).

1. Copy this whole folder from the USB stick to the PC's disk (the Desktop is fine).
   It runs much faster from the disk than from the stick.

2. Double-click "1 - Check this PC.bat".
   It says whether training will use the NVIDIA card or the processor, and how long it should take.
   If it says the driver is too old, update it and run it again.

3. Double-click "Preview album pages.bat" to see a few generated pages (red lines = what the model learns).

4. Double-click "2 - Make album pages.bat". It writes 8000 pages into the "albums" folder (about 350 MB).
   If it is stopped, run it again: it carries on where it stopped.

5. Double-click "3 - Train.bat". It shows each pass as it finishes; "photo overlap" should climb towards 90 % and
   more. You can close the window or press Ctrl+C at any time: running it again carries on after the last
   finished pass.

6. When it says "Training finished", copy the "models" folder back to the USB stick and bring it back.
   modelsest.dat is the trained network (about 8 MB); state.json says how it did.

Photos: public-domain (CC0) photographs from Smithsonian Open Access; see photos\CREDITS.csv.
No family photos are on this stick or used for training.

Windows may warn that the program is from an unknown publisher (it isn't signed): choose "More info", then
"Run anyway". If it says a DLL such as VCRUNTIME140.dll or MSVCP140.dll is missing, install the Microsoft Visual C++
Redistributable (x64): https://aka.ms/vs/17/release/vc_redist.x64.exe
