namespace MovieTelopTranscriber.App.Models;

public sealed record ProjectBundleLoadResult(
    string ProjectFilePath,
    string ExtractionDirectory,
    ProjectBundleManifest Manifest,
    ExportPackage ExportPackage,
    FrameExtractionResult FrameExtractionResult,
    IReadOnlyList<FrameAnalysisResult> FrameAnalyses);
