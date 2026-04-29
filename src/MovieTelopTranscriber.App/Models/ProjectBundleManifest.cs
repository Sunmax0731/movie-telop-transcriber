namespace MovieTelopTranscriber.App.Models;

public sealed record ProjectBundleManifest(
    string SchemaVersion,
    DateTimeOffset SavedAt,
    string ApplicationVersion,
    string SourceVideoPath,
    bool SourceVideoExistsAtSave,
    string RunId,
    string ExportPackagePath,
    string FramesDirectoryPath,
    string OcrDirectoryPath,
    string AttributesDirectoryPath,
    string? SelectedSegmentId,
    string? SelectedDetectionId,
    UserInterfaceSettings? Ui);
