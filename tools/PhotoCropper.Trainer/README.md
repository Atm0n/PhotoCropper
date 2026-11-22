# PhotoCropper trainer

Trains PhotoCropper's photo-detection model: a small network (a U-Net, about 1.9 million weights) that marks which
pixels of a scan are photo. It learns from **practice album pages** this tool generates, with real public-domain
photographs on them, so no family photos are ever used for training. Real scans are only used to *score* a model.

Not shipped with the app, and not part of `PhotoCropper.slnx`: its native libraries are large (LibTorch).

## Steps

```powershell
cd tools\PhotoCropper.Trainer

# 1. What will this computer use, and how long will it take?
dotnet run -c Release -- check

# 2. Public-domain photographs to put on the pages (about 1.5 GB → 1500 photos shrunk to ~200 KB each).
dotnet run -c Release -- photos --out photos --count 1500

# 3. Look at a few generated pages (mask outlines in red) before making thousands.
dotnet run -c Release -- generate --preview preview.jpg --photos photos

# 4. The practice set (the GPU plan: 8000 pages of 512 px; check says what suits this computer).
dotnet run -c Release -- generate --out albums --count 8000 --size 512 --photos photos
```

Training itself (`train`) and exporting the model come next.

Every step can be stopped and run again: `photos` skips the photos it already has, and `generate` skips the pages
already written. The same `--seed` and `--count` always give exactly the same pages.

## GPU or CPU

`dotnet build` (and `dotnet run`) picks the native library when building:

- **An NVIDIA driver is installed** (`nvidia-smi.exe` exists): the CUDA 12.8 build (`TorchSharp-cuda-windows`, a
  few GB to download the first time). The driver must be **570 or newer**; `check` says if it isn't.
- **Otherwise**: the CPU build (`TorchSharp-cpu`). Integrated graphics (AMD Radeon, Intel) can't be used.

Force one with `-p:TorchBackend=cuda` or `-p:TorchBackend=cpu`.

`check` runs a few real training steps on the chosen device and times them. If the card is found but a step fails
(this LibTorch build has no kernels for the card), it says so and falls back to the CPU. The fallback for that case is
a short Python training script producing the same model file; everything else stays as it is.

## Where the photos come from

[Smithsonian Open Access](https://www.si.edu/openaccess): images marked **CC0** (no copyright restrictions). Only
records whose object type is a photograph (photographs, photographic prints, cabinet cards, snapshots...) and whose
image is marked CC0 are taken, by default from the National Museum of African American History and Culture, the
National Portrait Gallery, the National Museum of American History and the Archives of American Art.

Everything comes from the Smithsonian's public AWS S3 bucket (`smithsonian-open-access`), not from the museums' web
sites or API, so there is no API key and no API quota. The tool is still polite: at most 4 downloads at a time,
a pause for everyone whenever the server answers 429 or 503 (as long as `Retry-After` says, or growing waits), a stop
after 25 failures in a row, a plain `PhotoCropperTrainer/1.0` user agent, and a cache so nothing is fetched twice.
`CREDITS.csv` lists every photo with its title and source.

Photos are shrunk to 1200 px on their long side when saved. The tutorial's photos (`tutorial-work/photos`) can be
added with `--photos photos,..\..\tutorial-work\photos`.

## What the pages vary

Scanner bed (white or grey lid, black, a dark bezel strip); album page or none, filling the scan or lying on the bed,
slightly turned; paper colour (cream, salmon, white, kraft, black, brown, green...), grain, blotches, printed dots or
lines, a frame line, a sleeve's glare; one to eight photos in a loose grid or scattered, touching or apart, mostly
nearly straight; print shape, white or paper-coloured borders, rounded corners, shadows, photo corners; black and
white, sepia, faded colour or fresh, flat contrast, some faded towards the paper's colour (the hard case); captions
and plain paper cut-outs, which are not photos; uneven lighting, blur, sensor noise and JPEG compression.

The mask covers each photo's whole box (border included) with a 1.5 px gap between touching photos, so they come
out as separate shapes.
