using Avalonia;
using System.Globalization;

namespace PhotoCropper.Gui.Services;

internal static class ResourceKeys
{
    public const string BtnOpenScans = nameof(BtnOpenScans);
    public const string BtnSaveResults = nameof(BtnSaveResults);
    public const string BtnAutoTune = nameof(BtnAutoTune);
    public const string BtnWorkDir = nameof(BtnWorkDir);
    public const string BtnNewProject = nameof(BtnNewProject);
    public const string BtnCheckUpdates = nameof(BtnCheckUpdates);
    public const string BtnResetBackground = nameof(BtnResetBackground);
    public const string BtnSampleBackground = nameof(BtnSampleBackground);

    public const string BtnDelete = nameof(BtnDelete);
    public const string BtnRotate = nameof(BtnRotate);
    public const string BtnDeleteCount = nameof(BtnDeleteCount);
    public const string BtnRotateCount = nameof(BtnRotateCount);
    public const string BtnSelectAll = nameof(BtnSelectAll);
    public const string BtnClearSelection = nameof(BtnClearSelection);
    public const string BtnSaveAndExit = nameof(BtnSaveAndExit);
    public const string BtnDiscardAndExit = nameof(BtnDiscardAndExit);
    public const string BtnCancel = nameof(BtnCancel);
    public const string BtnOk = nameof(BtnOk);

    public const string LblDetectionSensitivity = nameof(LblDetectionSensitivity);
    public const string LblAutoTuneOnPass = nameof(LblAutoTuneOnPass);
    public const string LblAutoAdjustOnLowCoverage = nameof(LblAutoAdjustOnLowCoverage);
    public const string LblCoveragePercent = nameof(LblCoveragePercent);
    public const string LblNoProject = nameof(LblNoProject);
    public const string TxtNoFiles = nameof(TxtNoFiles);
    public const string TxtSelectedCount = nameof(TxtSelectedCount);
    public const string PhotoCounter = nameof(PhotoCounter);
    public const string ScanCounter = nameof(ScanCounter);
    public const string ProcessingScan = nameof(ProcessingScan);
    public const string TxtCancelling = nameof(TxtCancelling);
    public const string TxtCheckingUpdates = nameof(TxtCheckingUpdates);
    public const string TxtUpdateAvailable = nameof(TxtUpdateAvailable);
    public const string TxtUpToDate = nameof(TxtUpToDate);
    public const string TxtUpdateError = nameof(TxtUpdateError);

    public const string TipRefine = nameof(TipRefine);
    public const string TipRefineMultiDisabled = nameof(TipRefineMultiDisabled);
    public const string TipActiveProject = nameof(TipActiveProject);
    public const string TipWorkDir = nameof(TipWorkDir);
    public const string TipAutoTune = nameof(TipAutoTune);
    public const string TipAutoTuneOnPass = nameof(TipAutoTuneOnPass);
    public const string TipAutoAdjustOnLowCoverage = nameof(TipAutoAdjustOnLowCoverage);

    public const string MsgUndo = nameof(MsgUndo);
    public const string MsgRedo = nameof(MsgRedo);
    public const string MsgRotating = nameof(MsgRotating);
    public const string MsgPhotoRotated = nameof(MsgPhotoRotated);
    public const string MsgBatchRotating = nameof(MsgBatchRotating);
    public const string MsgBatchRotated = nameof(MsgBatchRotated);
    public const string MsgPhotoDeleted = nameof(MsgPhotoDeleted);
    public const string MsgBatchDeleted = nameof(MsgBatchDeleted);
    public const string MsgScanning = nameof(MsgScanning);
    public const string MsgScanCancelled = nameof(MsgScanCancelled);
    public const string MsgScanFailed = nameof(MsgScanFailed);
    public const string MsgScanSuccess = nameof(MsgScanSuccess);
    public const string MsgScanDeleted = nameof(MsgScanDeleted);
    public const string MsgNoScanData = nameof(MsgNoScanData);
    public const string MsgNoScannerFound = nameof(MsgNoScannerFound);
    public const string MsgAutoTuning = nameof(MsgAutoTuning);
    public const string MsgAutoTuneSuccess = nameof(MsgAutoTuneSuccess);
    public const string MsgAutoTuneFailed = nameof(MsgAutoTuneFailed);
    public const string MsgDetectingPhotos = nameof(MsgDetectingPhotos);
    public const string MsgDetectionComplete = nameof(MsgDetectionComplete);
    public const string MsgNewProjectReady = nameof(MsgNewProjectReady);
    public const string MsgReprocessing = nameof(MsgReprocessing);
    public const string MsgSavingProgress = nameof(MsgSavingProgress);
    public const string MsgSaved = nameof(MsgSaved);
    public const string MsgAllScansAlreadySaved = nameof(MsgAllScansAlreadySaved);
    public const string MsgScannerNotFoundDetails = nameof(MsgScannerNotFoundDetails);
    public const string MsgScannerErrorDetails = nameof(MsgScannerErrorDetails);
    public const string MsgExtractingCrop = nameof(MsgExtractingCrop);
    public const string MsgManualCropAdded = nameof(MsgManualCropAdded);
    public const string MsgRefineModeHelp = nameof(MsgRefineModeHelp);
    public const string MsgApplyingRefine = nameof(MsgApplyingRefine);
    public const string MsgRefineSuccess = nameof(MsgRefineSuccess);
    public const string MsgRefineCancelled = nameof(MsgRefineCancelled);
    public const string MsgResumeSession = nameof(MsgResumeSession);
    public const string MsgUnsavedChanges = nameof(MsgUnsavedChanges);

    public const string TitleScannerNotFound = nameof(TitleScannerNotFound);
    public const string TitleScannerError = nameof(TitleScannerError);
    public const string TitleNoScanner = nameof(TitleNoScanner);
    public const string TitleResumeSession = nameof(TitleResumeSession);
    public const string TitleUnsavedChanges = nameof(TitleUnsavedChanges);

    public const string MenuExportSingle = nameof(MenuExportSingle);
    public const string LblOutputDir = nameof(LblOutputDir);
    public const string LblNamingPreview = nameof(LblNamingPreview);
    public const string TxtScanningSearching = nameof(TxtScanningSearching);
    public const string MsgClickToSample = nameof(MsgClickToSample);
    public const string MsgBackgroundSampled = nameof(MsgBackgroundSampled);

    public const string ColorCustom = nameof(ColorCustom);
    public const string AppButtonActionBrush = nameof(AppButtonActionBrush);
    public const string AppSubtleCardBrush = nameof(AppSubtleCardBrush);
    public const string AppAccentBrush = nameof(AppAccentBrush);
    public const string AppDangerTextBrush = nameof(AppDangerTextBrush);
    public const string AppSuccessTextBrush = nameof(AppSuccessTextBrush);
}

internal static class LocalizationService
{
    public static string GetString(string key, string fallback = "")
    {
        ArgumentNullException.ThrowIfNull(key);

        if (Application.Current != null && Application.Current.TryGetResource(key, null, out object? resource) && resource != null)
        {
            return resource.ToString() ?? fallback;
        }

        return fallback;
    }

    public static string Format(string key, string fallbackTemplate, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(fallbackTemplate);

        string template = GetString(key, fallbackTemplate);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.InvariantCulture, fallbackTemplate, args);
        }
    }

    public static bool TryGetResource<T>(string key, out T? resource) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);

        if (Application.Current != null && Application.Current.TryGetResource(key, null, out object? raw) && raw is T typed)
        {
            resource = typed;
            return true;
        }

        resource = null;
        return false;
    }
}
