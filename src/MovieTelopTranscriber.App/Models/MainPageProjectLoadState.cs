namespace MovieTelopTranscriber.App.Models;

public sealed record MainPageProjectLoadState(
    string CurrentProjectFilePath,
    string LoadedProjectExtractionDirectory,
    VideoMetadata LatestMetadata,
    FrameExtractionResult LatestFrameExtractionResult,
    IReadOnlyList<FrameAnalysisResult> LatestFrameAnalyses,
    IReadOnlyList<SegmentRecord> LatestSegments,
    double LatestFrameIntervalSeconds,
    IReadOnlyList<EditOperationRecord> TimelineEdits,
    MainPageStoredUiState UiState,
    string VideoPath,
    string WorkDirectoryText,
    string OcrEngineText,
    string ExportDirectoryText,
    string JsonOutputPathText,
    string SegmentsCsvOutputPathText,
    string FramesCsvOutputPathText,
    ExportWriteResult LatestExport,
    bool SourceVideoExists);

public sealed record MainPageProjectSaveState(
    double FrameIntervalSeconds,
    string OcrEngine,
    UserInterfaceSettings UiSettings,
    string? SelectedSegmentId,
    string? SelectedDetectionId);
