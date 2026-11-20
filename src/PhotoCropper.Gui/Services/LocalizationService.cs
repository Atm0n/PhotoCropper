using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using System.Globalization;

namespace PhotoCropper.Gui.Services;

internal static class ResourceKeys
{
    public const string BtnOpenScans = nameof(BtnOpenScans);
    public const string BtnAutoTune = nameof(BtnAutoTune);
    public const string BtnWorkDir = nameof(BtnWorkDir);
    public const string BtnCheckUpdates = nameof(BtnCheckUpdates);
    public const string BtnResetBackground = nameof(BtnResetBackground);
    public const string BtnSampleBackground = nameof(BtnSampleBackground);

    public const string BtnDelete = nameof(BtnDelete);
    public const string BtnRotate = nameof(BtnRotate);
    public const string BtnDeleteCount = nameof(BtnDeleteCount);
    public const string BtnDeletePhoto = nameof(BtnDeletePhoto);
    public const string LblDetectionSettings = nameof(LblDetectionSettings);
    public const string BtnRotateCount = nameof(BtnRotateCount);
    public const string BtnSelectAll = nameof(BtnSelectAll);
    public const string LblMarkedCount = nameof(LblMarkedCount);
    public const string BtnAdjustTrim = nameof(BtnAdjustTrim);
    public const string TipAdjustTrim = nameof(TipAdjustTrim);
    public const string BtnAutoTrim = nameof(BtnAutoTrim);
    public const string TipAutoTrim = nameof(TipAutoTrim);
    public const string LblStyleAll = nameof(LblStyleAll);
    public const string StyleBlackAndWhite = nameof(StyleBlackAndWhite);
    public const string StyleSepia = nameof(StyleSepia);
    public const string StyleFadedColor = nameof(StyleFadedColor);
    public const string StyleColor = nameof(StyleColor);
    public const string BtnSuggestedPresets = nameof(BtnSuggestedPresets);
    public const string TipSuggestedPresets = nameof(TipSuggestedPresets);
    public const string TipStyleFilter = nameof(TipStyleFilter);
    public const string LblSortingStyles = nameof(LblSortingStyles);
    public const string BtnClearSelection = nameof(BtnClearSelection);
    public const string BtnCancel = nameof(BtnCancel);
    public const string BtnOk = nameof(BtnOk);
    public const string BtnReviewPhotos = nameof(BtnReviewPhotos);
    public const string TipReviewPhotos = nameof(TipReviewPhotos);
    public const string TitleReviewMode = nameof(TitleReviewMode);
    public const string MsgReviewEmpty = nameof(MsgReviewEmpty);
    public const string ReviewSentinelTitle = nameof(ReviewSentinelTitle);
    public const string ReviewSentinelSubtitle = nameof(ReviewSentinelSubtitle);
    public const string ReviewSentinelStats = nameof(ReviewSentinelStats);
    public const string BtnReviewGoToStart = nameof(BtnReviewGoToStart);
    public const string TipToggleRightPanel = nameof(TipToggleRightPanel);

    public const string LblDetectionSensitivity = nameof(LblDetectionSensitivity);
    public const string LblAutoTuneOnPass = nameof(LblAutoTuneOnPass);
    public const string LblAutoAdjustOnLowCoverage = nameof(LblAutoAdjustOnLowCoverage);
    public const string LblCoveragePercent = nameof(LblCoveragePercent);
    public const string LblLookaheadPreloaded = nameof(LblLookaheadPreloaded);
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
    public const string MsgProjectLoaded = nameof(MsgProjectLoaded);
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
    public const string TitleExportFailed = nameof(TitleExportFailed);
    public const string MsgExportFailedStatus = nameof(MsgExportFailedStatus);
    public const string TitleUnexpectedError = nameof(TitleUnexpectedError);
    public const string MsgUnexpectedError = nameof(MsgUnexpectedError);
    public const string StageScanStatus = nameof(StageScanStatus);
    public const string StageCropsStatus = nameof(StageCropsStatus);
    public const string StageFinishStatus = nameof(StageFinishStatus);
    public const string StageNeedsProject = nameof(StageNeedsProject);
    public const string StartSubtitle = nameof(StartSubtitle);
    public const string BtnNewProject = nameof(BtnNewProject);
    public const string TipNewProject = nameof(TipNewProject);
    public const string BtnOpenProject = nameof(BtnOpenProject);
    public const string TipOpenProject = nameof(TipOpenProject);
    public const string BtnRestoreOnly = nameof(BtnRestoreOnly);
    public const string TipRestoreOnly = nameof(TipRestoreOnly);
    public const string LblProjectName = nameof(LblProjectName);
    public const string LblProjectLocation = nameof(LblProjectLocation);
    public const string BtnChangeLocation = nameof(BtnChangeLocation);
    public const string BtnCreateProject = nameof(BtnCreateProject);
    public const string LblRecentProjects = nameof(LblRecentProjects);
    public const string LblNoRecentProjects = nameof(LblNoRecentProjects);
    public const string TipRemoveRecent = nameof(TipRemoveRecent);
    public const string BtnBackToProject = nameof(BtnBackToProject);
    public const string LblProjectMissing = nameof(LblProjectMissing);
    public const string LblRecentProjectDetails = nameof(LblRecentProjectDetails);
    public const string DefaultProjectName = nameof(DefaultProjectName);
    public const string MsgProjectNameInvalid = nameof(MsgProjectNameInvalid);
    public const string MsgProjectExists = nameof(MsgProjectExists);
    public const string MsgProjectCreateFailed = nameof(MsgProjectCreateFailed);
    public const string MsgProjectInUse = nameof(MsgProjectInUse);
    public const string BtnImportScans = nameof(BtnImportScans);
    public const string BtnImportPhotos = nameof(BtnImportPhotos);
    public const string ScanStatusNotChecked = nameof(ScanStatusNotChecked);
    public const string ScanPreviewDetecting = nameof(ScanPreviewDetecting);
    public const string TipScanPreview = nameof(TipScanPreview);
    public const string ScanStatusNoPhotos = nameof(ScanStatusNoPhotos);
    public const string ScanStatusPhotos = nameof(ScanStatusPhotos);
    public const string FinishSourceImported = nameof(FinishSourceImported);
    public const string FinishStatusNew = nameof(FinishStatusNew);
    public const string FinishStatusStale = nameof(FinishStatusStale);
    public const string FinishStatusExported = nameof(FinishStatusExported);
    public const string MsgSavingCrops = nameof(MsgSavingCrops);
    public const string MsgCropsSaved = nameof(MsgCropsSaved);
    public const string MsgAutosaveFailed = nameof(MsgAutosaveFailed);
    public const string MenuScannerSettings = nameof(MenuScannerSettings);
    public const string TipScanF5 = nameof(TipScanF5);
    public const string MenuExportSettings = nameof(MenuExportSettings);
    public const string MenuResetDetection = nameof(MenuResetDetection);
    public const string TipHelpF1 = nameof(TipHelpF1);
    public const string MsgExportingFinal = nameof(MsgExportingFinal);
    public const string MsgFinalExported = nameof(MsgFinalExported);
    public const string MsgImportingPhotos = nameof(MsgImportingPhotos);
    public const string MsgScanReplaced = nameof(MsgScanReplaced);
    public const string MsgScannerNotFoundDetails = nameof(MsgScannerNotFoundDetails);
    public const string MsgScannerErrorDetails = nameof(MsgScannerErrorDetails);
    public const string MsgExtractingCrop = nameof(MsgExtractingCrop);
    public const string MsgManualCropAdded = nameof(MsgManualCropAdded);
    public const string MsgFindingPhoto = nameof(MsgFindingPhoto);
    public const string MsgNoPhotoFound = nameof(MsgNoPhotoFound);
    public const string MsgPhotoFound = nameof(MsgPhotoFound);
    public const string MsgPhotoRefitted = nameof(MsgPhotoRefitted);
    public const string BtnFindPhoto = nameof(BtnFindPhoto);
    public const string TipFindPhoto = nameof(TipFindPhoto);
    public const string MsgRefineModeHelp = nameof(MsgRefineModeHelp);
    public const string MsgApplyingRefine = nameof(MsgApplyingRefine);
    public const string MsgRefineSuccess = nameof(MsgRefineSuccess);
    public const string MsgRefineCancelled = nameof(MsgRefineCancelled);
    public const string MsgResumeSession = nameof(MsgResumeSession);

    public const string TitleScannerNotFound = nameof(TitleScannerNotFound);
    public const string TitleScannerError = nameof(TitleScannerError);
    public const string TitleNoScanner = nameof(TitleNoScanner);
    public const string TitleResumeSession = nameof(TitleResumeSession);

    public const string MenuExportSingle = nameof(MenuExportSingle);
    public const string LblOutputDir = nameof(LblOutputDir);
    public const string LblNamingPreview = nameof(LblNamingPreview);
    public const string LblScannerPageSize = nameof(LblScannerPageSize);
    public const string TipScannerPageSize = nameof(TipScannerPageSize);
    public const string TxtScanningSearching = nameof(TxtScanningSearching);
    public const string MsgClickToSample = nameof(MsgClickToSample);
    public const string MsgBackgroundSampled = nameof(MsgBackgroundSampled);
    public const string MsgBackgroundAutoTuned = nameof(MsgBackgroundAutoTuned);
    public const string MsgPhotoExported = nameof(MsgPhotoExported);
    public const string MsgPhotoExportFailed = nameof(MsgPhotoExportFailed);
    public const string AppTitle = nameof(AppTitle);
    public const string LblQualityMax = nameof(LblQualityMax);

    public const string ColorCustom = nameof(ColorCustom);
    public const string AppButtonActionBrush = nameof(AppButtonActionBrush);
    public const string AppSubtleCardBrush = nameof(AppSubtleCardBrush);
    public const string AppAccentBrush = nameof(AppAccentBrush);
    public const string AppDangerTextBrush = nameof(AppDangerTextBrush);
    public const string AppSuccessTextBrush = nameof(AppSuccessTextBrush);
}

internal static class LocalizationService
{
    private static readonly Dictionary<string, string> AvailableLanguages = new()
    {
        { "en-US", "English" },
        { "es-ES", "Español" },
        { "ca-ES", "Català" }
    };

    public static string CurrentLanguage { get; private set; } = "en-US";

    public static void SetLanguage(string languageCode)
    {
        if (!AvailableLanguages.ContainsKey(languageCode))
        {
            languageCode = "en-US";
        }

        var translations = Application.Current?.Resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .FirstOrDefault(d => d.Source?.ToString().Contains("i18n", StringComparison.Ordinal) == true);

        if (translations != null)
        {
            Application.Current?.Resources.MergedDictionaries.Remove(translations);
        }

        Application.Current?.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri($"avares://PhotoCropper.Gui/Assets/i18n/{languageCode}.axaml"))
        {
            Source = new Uri($"avares://PhotoCropper.Gui/Assets/i18n/{languageCode}.axaml")
        });

        CurrentLanguage = languageCode;

        if (SettingsManager.Instance.Settings.Language != languageCode)
        {
            SettingsManager.Instance.Settings.Language = languageCode;
            SettingsManager.Instance.Save();
        }
    }

    public static void Initialize(string? preferredLanguage = null)
    {
        if (!string.IsNullOrEmpty(preferredLanguage) && AvailableLanguages.ContainsKey(preferredLanguage))
        {
            SetLanguage(preferredLanguage);
            return;
        }

        string localCulture = CultureInfo.CurrentCulture.Name;
        var match = AvailableLanguages.Keys.FirstOrDefault(k => k.Equals(localCulture, StringComparison.OrdinalIgnoreCase))
                 ?? AvailableLanguages.Keys.FirstOrDefault(k => k.StartsWith(localCulture.Split('-')[0], StringComparison.OrdinalIgnoreCase));

        SetLanguage(match ?? "en-US");
    }

    public static IReadOnlyList<(string Code, string Name)> GetAvailableLanguages()
    {
        return AvailableLanguages.Select(kv => (kv.Key, kv.Value)).ToList();
    }

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
