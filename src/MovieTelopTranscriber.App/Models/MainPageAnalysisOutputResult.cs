namespace MovieTelopTranscriber.App.Models;

public sealed record MainPageAnalysisOutputResult(
    ExportWriteResult Export,
    RunLogWriteResult LogWriteResult,
    RunPerformanceSummaryRecord PerformanceSummary,
    int DetectionCount,
    int WarningCount,
    int ErrorCount,
    ProcessingError? FirstOcrError);
