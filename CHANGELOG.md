# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Tutorial** ([docs/TUTORIAL.md](docs/TUTORIAL.md)): a step-by-step walkthrough of a whole album, from scanning to exported photos, with screenshots of every stage. The README now shows the app at the top. All sample photos are public domain.
- **Rotate in Finish**: ↺ ↻ in the editor (or R), and "Rotate 90°" for the selected photos, for the few photos whose orientation the Crops stage missed. The photo itself turns (losslessly), and so does its box on the scan, so Crops shows it the same way; its repair strokes and hand-set trim turn with it.
- **Adjust the trim by hand** (Finish editor, Border section): **Adjust** shows the trim as a frame on the photo; drag its edges, or a corner for two at once, to set exactly what the final photo keeps, with a magnifier showing the edge while you drag. **Automatic** goes back to the border found automatically. The frame belongs to the photo: presets and "Copy settings" keep each photo's own. What the trim leaves out is now shaded live in the viewer.
- **Photo types in Finish**: each photo is labelled black & white, sepia, faded colour or colour, and the chips above the grid show one type at a time, with how many photos each has. "Select all" then selects just that type. **Suggested by type** gives each selected photo the preset that suits its kind of print: Black & white, Sepia, Faded colour print, or Only clean dust for colour photos in good shape. Types are told from the photos' colours; aged or toned black-and-white prints and prints on tinted album pages count as black and white.

- **Dim outside the boxes** (Crops, at the bottom of Box Color): darkens the scan outside the boxes, so what will be cropped stands out. Off by default; the choice is remembered.

### Changed
- **Exporting final photos is about 3 times quicker** (Finish → Export, and the command line's restore stage). Several photos are restored and encoded side by side, then written in order, so every file gets the same name as before. On 171 photos from 60 real album scans: 10 s instead of 35 s, with byte-identical files.
- **Find Photo is about a third quicker.** A click tries its foreground masks side by side on all processor cores instead of one after another. On 60 real album scans (191 photos, clicked one by one): 0.34 s per click on average instead of 0.55 s, at most 0.7 s instead of 4.1 s, with exactly the same box found every time.
- **Opening a new scan in Crops is about 40% quicker** with Auto-Tune on. Auto-Tune tries its settings side by side on all processor cores instead of one after another, and detection's passes do the same. When Auto-Tune keeps the settings the scan was already detected with, the photos found are kept instead of detected again. On 60 real album scans: 57 s instead of 99 s in all, with exactly the same boxes on every scan.
- **Calmer boxes in Crops.** Each box is a thin line with a dark edge, drawn just outside its photo so the photo's own edge stays visible to match against. Only the selected box shows its corners, rotation handle and red ×: click inside a box to select it. New installs draw the line in soft white instead of red (Box Color: Soft White); a colour already chosen is kept.
- **Finding a photo is just a click, with no Find Photo button.** In Crops, clicking a spot on the scan that isn't in a box finds the photo there and adds its box. Clicking inside a box selects its photo, and its red × deletes it, as before. To redo a wrong box, delete it and click the photo. Corners, the rotation handle and drawing a box work as before, and a click may move a little (up to 8 px) and still count as a click.
- **CLI interactive wizard** (`-w`): it first asks what to do (crop and restore in one step, crop only, restore already-cropped photos, or add scans to an app project) and then only asks what that needs. It also asks whether to trim prints' white borders.
- **Finish, several photos at once**: the bar now has a checkbox each for automatic restoration, dust removal and border trimming, in place of the "Trim border: on / off" buttons. Each shows whether all, none or (with a dash) only some of the selected photos have it, and one click turns it on for all of them, or off when all had it.

### Fixed
- **Clicking a white-bordered print on an album page finds the whole print.** When the print's border was close to the scanner bed's colour, the box often stopped at the picture inside the border. The border (a plain band between the picture and the page's paper) is now included. On 30 generated prints, 17 are found whole (9 before); on 191 photos of real album scans, Find Photo's results are unchanged.
- **Photos on light album pages are found one by one.** On cream or salmon pages lying on the scanner bed, whose faded photos are close to the paper's colour, detection took the whole page as one photo. Such a page is now recognised (a large area of light, soft-coloured paper on the bed) and its paper used as the background, as Pick Color would. A page still taken whole is searched again against its paper, and as a last resort its photos are taken as the rectangular holes in the paper. On 22 real light album pages, 46 of 68 photos are now boxed as the user boxed them (none before); other scans are unchanged.
- **Photos on a dark album page lying on a light scanner bed are found.** Detection and Auto-Tune found none of them at a low sensitivity, or missed some at the default one: the ring of bed around the page closed one outline around everything on it, the page's colour was sampled on its darker edge, and the colour of grey or black paper was matched by its hue, which on neutral paper is only the scanner's noise. The bed and the page's edge now count as background, the page's colour is sampled inside its edge, and hue is ignored on near-grey backgrounds. With the default settings, every photo on the tutorial's demo album is now found (9 of 12 before).
- **A scan's photos could be listed twice in Crops** (e.g. "photo 1 of 14" for 7 photos), most often on the first scan right after opening a project. The Scan stage's preview and Crops could detect the same scan at once and both fill its list. They now take turns. What was saved and exported was not affected.
- **A photo turned in Finish no longer turns back.** On a scan edited in Crops earlier in the session, going back to Crops and then to Finish again restored the photo's old orientation, since the scan's older boxes were saved over the turn. The scan is now reloaded from its saved boxes after a turn.
- **Automatic orientation no longer turns upright photos the wrong way.** Photos without faces (a street, a crowd, a faded print) could be turned sideways or upside down on weak hints from their colours, and a single doubtful "face" in a crowd could turn a photo upside down. Now a photo is only turned for a confident face or a clear sky over land; otherwise it stays as it was placed on the page. On 27 test photos none is turned wrongly any more (4 were before), and photos lying on their side or upside down are turned back as often as before.
- **Trim white border works on prints from dark album pages.** When the box around a bordered print also held some of a dark (or coloured) album page (all round it in a box drawn by hand, or in the corners of a tilted print), the border wasn't found and nothing was trimmed. The page around the print is now recognised and left out along with the border. Photos without a border are still never cut into.
- **Old projects** get each photo's orientation detected again when they are upgraded (opened for the first time in 3.x), instead of keeping one an older version saved, which could be wrong or missing. Photos turned by hand keep theirs.
- **Duplicate photos in Final/** ("name (1).jpg" next to "name.jpg"): exporting now replaces what is in the project's Final/ folder. The new file takes the place of the photo's previous one and of anything else under its name, with its " (1)" copies, unless another photo of the project owns it (left by an older version, or by an export whose record was lost). An export to another folder still never overwrites anything.
- **A project open in two windows** (or in the app and the command line) could lose records, since each saved its own copy of the project over the other's, e.g. of photos just exported, which then got exported again as copies. A project now opens in one place at a time; the start screen says when it is open elsewhere.
- A crash (*Cannot access a disposed object*) when the preview was rendered again while dragging a trim edge with the magnifier showing.
- **Trim white border** now finds the border on far more real prints. A crop often holds a band of album page or a sliver of scanner bed outside the print, scalloped (deckle) edges cast small shadows, and worn corners show the bed: each of these stopped it before. Now everything light around the picture (bed, page, border) is trimmed up to where the picture starts, and a print with a pale background along one edge is no longer mistaken for a bordered one. On a real 126-photo album it trims 18 of its 22 bordered prints, against 11 before, and still none of the 74 without a border.
- Finish export now shows the completion notification and flashes the taskbar, as the export settings say; both settings did nothing before.

## [3.2.0] - 2026-11-20

### Added
- **Trim white border** (Finish stage, per photo; off by default): the final photo leaves out the print's white or cream border and keeps only the picture; the preview shades what will be cut, and the cropped photo always keeps the whole print. It's cautious: a border must frame the picture on every side and end at a clean edge, so photos without one (light skies, sheets, vignettes fading to white) are never cut into, though some worn or scalloped borders are left in place. Presets keep each photo's choice.
- **Several photos at once in Finish**: Ctrl+click or Shift+click photos in the grid (or tick them), then one click applies a preset, turns border trimming on or off, or exports just those photos. With a photo open, "Copy settings to selected" gives the selection its settings.
- **CLI `--trim-borders`**: the same border trimming for the one-step batch, `--stage restore` and new photos in `--project` mode (`--stage crop` keeps the whole print).

### Fixed
- **The `.deb` couldn't be installed on Ubuntu 26.04** (or any release newer than its list of ICU versions): it required one of `libicu66`–`libicu76`, and 26.04 ships `libicu78`. ICU is now only recommended (so `apt` still installs it), and the `photocropper` and `photocropper-cli` launchers run the app without culture data where it's missing, as the AppImage already did. The release's package tests now run on Ubuntu 24.04 and 26.04.

## [3.1.0] - 2026-11-19

### Added
- **Magnifier in the Crops stage**: while you drag a box's corner or draw a new box, a zoomed view of the spot under the pointer appears in a corner of the scan view, with the boxes drawn as thin lines, so an edge can be put exactly on the photo's edge without zooming in.
- **Find Photo tool (🎯) in the Crops stage**: click inside a photo the detection missed and its crop box is added; click inside an existing box and it is re-fitted to the photo. Works on scans whose photos cover the scan's edges and on album pages, including photos stuck to the page or reaching its edge. Undo and Esc work as usual.
- Every download (installer, AppImage, .deb, zips) now includes `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` with the copyright and license text of every bundled component (Avalonia, OpenCvSharp/OpenCV, FFmpeg, ONNX Runtime, NAPS2, ImageSharp, the Inter font and the YuNet face model), plus ONNX Runtime's own notices.

### Changed
- **OpenCV is now provided by OpenCvSharp5 (OpenCV 5.1) instead of Emgu CV (OpenCV 5.0).** Detection results are the same: on 58 real scans every photo is found with the same orientation, and boxes match to within a few pixels. Existing projects open unchanged. On Linux, OpenCV now needs only the system C and C++ libraries (no GTK, FFmpeg, TBB or VTK packages).

### Fixed
- **Linux packages (CLI and GUI zips, `.deb`, AppImage) didn't include OpenCV in 3.0.0**, so they couldn't open a scan. The OpenCvSharp library is now part of every Linux package.
- The Windows uninstaller now removes the downloaded AI models and crash reports from `%LocalAppData%\PhotoCropper`, and asks whether to remove your settings too (default: keep them). Projects and photos are never touched.
- The README listed the YuNet face model as Apache 2.0; it is MIT licensed.
- **CLI found no photos on pages holding several prints**: its smallest-photo size defaulted to 25% of the scan (and 10–25% in the wizard's presets), so any photo smaller than a quarter of the page was ignored. It now defaults to 1%, as in the app.

## [3.0.0] - 2026-11-18

> **Upgrading from 2.x:** a project is converted the first time 3.0 opens it (exports move from `Cropped/` to `Final/`, and `session.json` gains each photo's history and Finish settings). Back up a project before opening it if you may need 2.x again: version 2.9 still opens a converted project, but saving it there drops the photo list, Finish settings and export records; 3.0 then crops the photos again from the scans, with default Finish settings.

### Added
- **Folder pipeline for projects**: `RawScans/` → `Cropped/` (one lossless PNG per photo, geometry only) → `Final/` (restored, with metadata). Photos get permanent IDs (`scan_0003_p02`), so re-cropping keeps their settings and replaces their files; `session.json` (format 2.0) records each photo's history and knows when a final photo is out of date. Existing projects are migrated automatically: their exports move from `Cropped/` to `Final/`.
- **Stage bar in the GUI (1 Scan → 2 Crops → 3 Finish)**: Scan shows every scan as a thumbnail card with its photo count, imports scan files into the project, and lets you **rescan** a page (the new scan takes its place) or **discard** it (two clicks; removes the file and its photos) while scanning; Crops is the scan editor (restoration moved out of it); Finish shows every cropped photo with its own restoration settings and status, a **before/after preview** of the selected photo (updating as settings change), exports what changed to `Final/`, and imports photos that are already cropped. Moving to Finish saves the crops of changed scans to `Cropped/`.
- **Finish photo editor**: clicking a photo opens it full-window: a large zoomable, pannable view with a draggable **before/after split** (hold Space for the original), the photo's settings and the repair brush in a sidebar, and a filmstrip to switch photos (←/→, Esc back to the grid). Previews render at up to 2048 px instead of 1000, so detail can be judged when zoomed; the brush paints on the same view.
- **AI fill for the repair brush (optional)**: *Fill: Quick | AI*. AI strokes are filled by the LaMa inpainting model, which continues the surrounding structure, for large damage and torn corners. The 208 MB model is a one-time download on request (pinned revision, SHA-256 verified, cancellable, with progress), runs locally with ONNX Runtime and is loaded in the background when AI fill is chosen; results are cached across preview updates. Strokes remember their fill method; without the model they fall back to the quick fill.
- **Repair brush (Finish stage)**: paint over scratches, creases, tears or stains on a large preview; they are filled in from their surroundings before colour restoration. Undo stroke and Clear repairs; strokes are saved per photo and re-applied on every export, and never copied by "Copy settings to all".
- **Restoration options per photo (Finish stage)**: presets (Faded colour print, Black & white, Sepia, Only clean dust, None), automatic restoration strength (Gentle/Normal/Strong) with optional colour-cast, contrast and faded-colour steps, automatic black-and-white/sepia detection or a chosen photo type, manual brightness/contrast/warmth/tint/saturation sliders, and "Copy settings to all". Dust removal no longer requires colour restoration. Older projects keep their settings.
- **CLI `--stage crop|restore`**: run only the crop stage (lossless PNGs, no restoration) or only the restore stage on any folder of photos, including photos cropped elsewhere.
- **Crash reports**: Unhandled errors are written to `%LocalAppData%/PhotoCropper/logs` (last 20 kept). Errors raised by UI actions show an error dialog with the report location and keep the app open so unsaved work can be saved.
- **Export failure report**: Bulk export lists the scans that failed to export instead of only logging them.
- Session crops now record each photo's auto-orientation (`AutoRotationDegrees`) and each scan's exported files (`ExportedFiles`) in `session.json`. Older sessions still load.
- **CLI project mode** (`--project <folder>`): runs the app's RawScans → Cropped → Final pipeline on a project folder. New scans are added, boxes edited in the app are kept, the rest are detected like the app does, and the project opens in the app exactly as the CLI left it. Re-running only writes what changed.
- **Simpler CLI options**: `--auto-tune [mismatch|always|low-coverage|off]` replaces three auto-tune flags; `--no-restore`, `--no-dust` and `--no-orient` turn features off; `--help` is grouped and lists one name per option. Unknown options and invalid values now print a warning instead of being ignored silently. All older option names still work.

### Changed
- **Start screen at every launch**: New project (name and location; the folder is created), Open project, Restore photos only (a new project that opens the Finish stage's photo import), and recent projects with a thumbnail and their scan and photo counts (missing folders are dimmed and can be removed from the list). The project button on the top bar opens it again to switch project. The last project is no longer reopened silently, and scanning or importing no longer asks for a folder.
- **Scan stage shows the selected scan large**, with the photos found on it outlined and numbered, beside a list of all scans; a new scan is selected as soon as it arrives, so a crooked or overlapping page can be spotted and rescanned at once. The outlines come from the Crops stage's own detection (the scan is then ready there), or from the saved boxes; Rescan, Discard and Adjust crops act on the selected scan, and the Scan button has a ⚙ for the scanner settings.
- **Automatic saving; no Save button**: every crop edit is written to `session.json` a moment after it is made and before closing or switching project, so closing the app or a crash loses nothing. Reopening a project restores each scan's boxes as they were left (before, only exported scans kept their boxes). The Save button and `Ctrl+S` (a bulk export, now done by the Finish stage), the unsaved-changes prompt on close, and the export settings dialog's "Export now" and "always show before saving" options are gone.
- **One top bar**: project, the three stages, Undo/Redo, Help and a **Settings** menu (scanner and export settings, theme, language, reset detection settings, check for updates). Removed duplicates: the toolbar Scan button (it is in the Scan stage, with a ⚙ for the scanner settings; `F5` works anywhere), the project label, theme and language menus and shortcut list in the status bar, Reset Defaults on the toolbar, and the hidden `Alt` menu bar.
- **One shortcut per action**: arrows, `Delete`, `R`, `Enter`, `Esc`, `Space`, `Ctrl+Z/Y`, `Ctrl+A`, `F1` and `F5`. Removed: `V`, `N`/`Ñ`, `X`, `B`, `Shift+Delete`, `PageUp/PageDown`, `E`/`L`/`Backspace` in review, `A`/`C`/`Backspace` in refine, and `B`/`F` in the Finish editor; their actions all have buttons. Review mode's "Edit in Scanner" button, which did the same as Close, is gone.
- **Crops stage**: the photos are always a grid of larger thumbnails (the Single/Grid switch is gone; Review mode is the full-size view), the detection sliders start collapsed behind "Detection settings", the Delete buttons say "Delete scan" and "Delete photo", and the stage bar shows "N of M scans checked".
- **Import and drag-and-drop go into a project**: they ask for a project folder when none is open, as Scan does, and dropped files are copied into `RawScans/` instead of replacing the project's scans.
- **Remaining English-only interface text is now translated** (Catalan, Spanish): the main menu titles, Undo/Redo, the window titles, the Crops stage and review mode tooltips and labels, the "no photos detected" message, the custom colour, export and scanner dialogs, and the single-photo export messages.
- **Help window rewritten for the new workflow**: a "How it works" overview of the three stages and their folders, cards grouped by stage (new: background colour, review mode, photo editor, repair brush and AI fill), corrected texts (where Save writes, box editing, per-photo restoration), and separate shortcut lists for the Crops stage and the Finish editor. The Crops toolbar labels (Background, Pick Color, Reset, Box Color) are now translated, and English labels consistently use "color".
- **Picking a background colour auto-tunes detection** for that colour instead of re-detecting with the previous settings, and the scan, boxes and settings now refresh after picking or resetting the colour (before, the view could stay on the old result).
- **Box overlays are drawn by the GUI** over the untouched scan instead of being painted into a copy of it by the engine. Dragging, resizing or rotating a box only repaints the overlay (the scan bitmap is no longer rebuilt on every mouse move), changing the box colour no longer re-runs detection, and each loaded scan no longer keeps a second full-resolution copy in memory. Lines are anti-aliased, and clicks are tested against the same geometry the overlay draws.
- **Re-export cleanup** only removes files this scan previously exported: the files recorded by its last export (if unchanged on disk and still in the output folder). Without a record it falls back to files named exactly as the current pattern produces, except for patterns without `{original}`/`{filename}` and scans sharing a base name and output folder with another scan. Unrelated files and other scans' exports are no longer deleted, and the source scan is never deleted.
- **Crop refinement** now also moves the photo's box on the scan, so refinements are shown on the overlay and kept with the session.
- **Manual crops** go through the same auto-orientation and colour restoration as detected photos.
- **Box overlays** (lines, handles, badges) and their click areas scale with scan resolution instead of using fixed pixel sizes.
- **Cancelling** an operation waits for it to stop (showing "Cancelling..."); a scan that ignores cancellation is abandoned after 5 seconds and its late result discarded.
- Face detection reuses one detector per thread instead of loading the model four times per photo, and the model file is written atomically.
- Build: warnings are treated as errors again; solution-wide preview features removed; `System.Drawing.Common` pinned to 11.0.0; unused `Avalonia.Diagnostics` removed.

### Fixed
- A scan whose photos were all deleted is no longer detected again when the project is reopened; it stays empty.
- Reopening a project no longer rewrites some cropped photos (and re-renders their final photos) because restoring a saved box gave slightly different pixels than detecting it.
- The Zoom entry in the Help window had no description in any language.
- Photos deleted or rotated in Review mode now also update the open scan's boxes and coverage when review closes.
- Deleting photos from the photo panel left their boxes drawn on the scan until it was reloaded; the boxes and coverage now update at once. Editing, deleting or refining a box keeps that photo shown instead of jumping back to the first one.
- **Dust removal removed almost nothing**: its edge guard cancelled every candidate, because a speck's own outline is an edge. Candidates are now only kept when other edges run past them, and the detection size scales with resolution, so larger dust on high-resolution scans is found too.
- **Automatic restoration amplified colour casts**: the levels stretch applied one curve to all colour channels, which multiplied the differences between them (a yellowed, low-contrast print came out more yellow). It now stretches lightness only.
- CI and release workflows installed the .NET 10 SDK while projects target .NET 11.
- Bulk export kept every exported scan loaded in memory.
- Export completion message always reported 0 saved photos.
- "Apply year to all scans" dropped the photographer and exact date from exported metadata.
- Non-ASCII characters in the EXIF photographer and description were written as `?` (e.g. "Josep Mart?").
- A write failure partway through a re-export left the files already written unrecorded.
- Undoing a delete lost the photo's manual rotation and hold-to-compare copy; dragging a box too small left it mismatched with its photo.
- "Save & Exit" closed the app even when some scans failed to export.
- Scanner errors ("scanner not found", timeouts) showed a generic error and skipped the scanner reset.
- Re-detecting a scan kept stale manual rotations; dropped extractions misaligned photos and boxes; resizing a box lost its manual rotation; refinement and resize desynchronised the hold-to-compare copy.
- Undo: counter-clockwise rotations undid in the wrong direction; undoing a rotation left the compare copy and saved rotation rotated; recorded box lists could change after being recorded.
- A new project without scans showed "Finish · Needs a project"; the status bar kept "Saving crops (0 of N)..." after the crops were saved; opening a project said "Scan acquired" instead of how many scans were loaded; the Discard confirmation ran past the edge of the scan card.
- Closing Review mode on another scan's photo opened that scan on its first photo instead of the one reviewed.

## [2.9.0] - 2026-09-28

### Added
- **Batch Review Mode**: A new full-screen overlay for rapidly reviewing, rotating, and curating detected photos across multiple scans (shortcut: `V`).
- **Collapsible Right Panel**: You can now hide the right-side configuration panel to maximize the workspace area while scanning.
- **Edit in Scanner**: Press `E` or double-click a photo in Review Mode to instantly jump to the editor for manual edge refinement.
- **Review Mode Mouse Support**: Use the mouse scroll wheel to quickly navigate through the batch review carousel.
- **Live Undo/Redo Sync**: Pressing `Ctrl+Z` or `Ctrl+Y` while in Review Mode now seamlessly syncs and updates the carousel live.
- `--always-auto-tune` CLI flag and interactive wizard option to unconditionally auto-tune all scans.
- Unit tests for `AutoTuneService` detection logic, sweep combinations, and scoring boundaries.
- Unit tests for `PhotoCropperEngine` including `ApplyGrabHandleResize` behavior and edge cases.
- Unit tests for `AutoOrientationService` covering landscape analysis heuristics (sky, water, vegetation).

### Changed
- **Default AI Behaviors**: Destructive AI enhancements (Restore Colors and Remove Dust & Scratches) are now disabled by default to prevent unwanted artifacts. Auto-Orientation remains enabled.
- **Scan Navigation**: Scan cycling now clamps at the first and last scans rather than looping infinitely, preventing users from losing track of newly added scans.
- Split the monolithic GitHub Actions pipeline into dedicated `ci.yml` (for testing PRs/pushes) and `release.yml` (for CD tagging) to adhere to security best practices.

### Fixed
- Replaced magic floating array indexes in `FaceOrientationService` with explicitly defined ONNX YuNet constants.
- Resolved orphaned exceptions caused by unawaited fire-and-forget background tasks by properly attaching fault continuations.
- Removed hardcoded fallback version (`2.7.1`) in the CI release workflow to prevent silent publication of incorrect versions on tag parse failure.

## [2.7.1] - 2026-09-26

### Fixed
- Fixed Windows installer build by ignoring missing GUI files.

## [2.7.0] - 2026-09-25

### Added
- Scanner bed auto-detection.
- Configurable bed page size.
- Cut-off prevention for photos scanned near the edge.

## [2.6.0] - 2026-09-25

### Added
- Background lookahead pre-processing.
- Selective low-coverage auto-tuning.
- OpenCV 5 runtime integration.

## [2.5.2] - 2026-09-21

### Fixed
- Minor bug fixes and stability improvements.

## [2.5.1] - 2026-09-21

### Fixed
- Minor patches for the 2.5.0 auto-tune overhaul.

## [2.5.0] - 2026-09-21

### Added
- Auto-tune detection overhaul.
- Sensitivity calibration.
- Dynamic versioning support.
- Centralized localization.

## [2.4.0] - 2026-09-18

### Added
- Workspace recovery mechanisms.
- Safe export tracking.

### Changed
- Clean single-file builds.
- General scanner improvements.

## [2.3.0] - 2026-09-12

### Added
- Unicode path support.
- Lazy scan loading for improved memory footprint.
- Bounded batch concurrency.
- Notification chime upon completion.

## [2.2.0] - 2026-09-12

### Changed
- Core version bump and minor internal refinements.

## [2.1.0] - 2026-09-11

### Added
- AI Face & Landscape Orientation.
- Color Restoration and Dust Inpainting.
- Gallery Grid View for better photo management.

## [2.0.0] - 2026-09-11

### Added
- Unattended CLI mode.
- High-Performance GUI.

### Changed
- Extracted logic into a modular core architecture.

## [1.0.0] - 2026-05-12

### Added
- Initial stable release.
- Cross-platform publish script.
- Linux runtime support.
