using System.IO.Compression;
using System.Text.Json;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public sealed class ProjectBundleService
{
    private const string SchemaVersion = "1.0.0";
    private const string ProjectManifestPath = "project.json";
    private const string BundleRootPath = "bundle";

    private readonly ExportPackageWriter _exportPackageWriter = new();

    public async Task SaveAsync(
        string projectFilePath,
        VideoMetadata sourceVideo,
        FrameExtractionResult frameExtractionResult,
        IReadOnlyList<FrameAnalysisResult> frameAnalyses,
        IReadOnlyList<SegmentRecord> segments,
        IReadOnlyList<EditOperationRecord> edits,
        double frameIntervalSeconds,
        string ocrEngine,
        UserInterfaceSettings uiSettings,
        string? selectedSegmentId,
        string? selectedDetectionId,
        CancellationToken cancellationToken = default)
    {
        var stagingRoot = Path.Combine(
            Path.GetTempPath(),
            "MovieTelopTranscriber",
            "project-save",
            Guid.NewGuid().ToString("N"));
        var bundleRoot = Path.Combine(stagingRoot, BundleRootPath);
        var runDirectory = Path.Combine(bundleRoot, frameExtractionResult.RunId);
        var framesDirectory = Path.Combine(runDirectory, "frames");
        var ocrDirectory = Path.Combine(runDirectory, "ocr");
        var attributesDirectory = Path.Combine(runDirectory, "attributes");

        Directory.CreateDirectory(framesDirectory);
        Directory.CreateDirectory(ocrDirectory);
        Directory.CreateDirectory(attributesDirectory);

        try
        {
            var stagedFrames = new List<ExtractedFrameRecord>(frameAnalyses.Count);
            var stagedAnalyses = new List<FrameAnalysisResult>(frameAnalyses.Count);

            foreach (var analysis in frameAnalyses)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frameFileName = Path.GetFileName(analysis.Frame.ImagePath);
                var stagedFramePath = Path.Combine(framesDirectory, frameFileName);
                File.Copy(analysis.Frame.ImagePath, stagedFramePath, overwrite: true);

                var stagedFrame = analysis.Frame with { ImagePath = stagedFramePath };
                stagedFrames.Add(stagedFrame);
                stagedAnalyses.Add(analysis with { Frame = stagedFrame });

                var responsePath = Path.Combine(ocrDirectory, $"{analysis.Ocr.RequestId}.response.json");
                await using (var stream = File.Create(responsePath))
                {
                    await JsonSerializer.SerializeAsync(stream, analysis.Ocr, OcrContractJson.OcrWorkerResponse, cancellationToken);
                }

                var attributesPath = Path.Combine(attributesDirectory, $"{analysis.Ocr.RequestId}.attributes.json");
                await using (var stream = File.Create(attributesPath))
                {
                    await JsonSerializer.SerializeAsync(stream, analysis.Attributes, OcrContractJson.AttributeAnalysisResult, cancellationToken);
                }
            }

            var stagedFrameExtractionResult = new FrameExtractionResult(
                frameExtractionResult.RunId,
                runDirectory,
                framesDirectory,
                stagedFrames);

            var exportWriteResult = await _exportPackageWriter.WriteAsync(
                sourceVideo,
                stagedFrameExtractionResult,
                stagedAnalyses,
                segments,
                edits,
                frameIntervalSeconds,
                ocrEngine,
                processingTimeMs: null,
                warningCount: 0,
                errorCount: 0,
                cancellationToken);

            var manifest = new ProjectBundleManifest(
                SchemaVersion,
                DateTimeOffset.Now,
                typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                sourceVideo.FilePath,
                File.Exists(sourceVideo.FilePath),
                frameExtractionResult.RunId,
                Path.GetRelativePath(stagingRoot, exportWriteResult.JsonPath),
                Path.GetRelativePath(stagingRoot, framesDirectory),
                Path.GetRelativePath(stagingRoot, ocrDirectory),
                Path.GetRelativePath(stagingRoot, attributesDirectory),
                selectedSegmentId,
                selectedDetectionId,
                CloneUiSettings(uiSettings));

            var manifestPath = Path.Combine(stagingRoot, ProjectManifestPath);
            await using (var stream = File.Create(manifestPath))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, ProjectBundleJsonContext.Default.ProjectBundleManifest, cancellationToken);
            }

            var projectDirectory = Path.GetDirectoryName(projectFilePath);
            if (!string.IsNullOrWhiteSpace(projectDirectory))
            {
                Directory.CreateDirectory(projectDirectory);
            }

            if (File.Exists(projectFilePath))
            {
                File.Delete(projectFilePath);
            }

            ZipFile.CreateFromDirectory(stagingRoot, projectFilePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    public async Task<ProjectBundleLoadResult> LoadAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        var extractionRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MovieTelopTranscriber",
            "project-cache",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractionRoot);
        ZipFile.ExtractToDirectory(projectFilePath, extractionRoot);

        var manifestPath = Path.Combine(extractionRoot, ProjectManifestPath);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("Project manifest was not found in the project file.");
        }

        ProjectBundleManifest? manifest;
        await using (var stream = File.OpenRead(manifestPath))
        {
            manifest = await JsonSerializer.DeserializeAsync(stream, ProjectBundleJsonContext.Default.ProjectBundleManifest, cancellationToken);
        }

        if (manifest is null)
        {
            throw new InvalidOperationException("Project manifest could not be read.");
        }

        var exportPackagePath = Path.Combine(extractionRoot, manifest.ExportPackagePath);
        if (!File.Exists(exportPackagePath))
        {
            throw new InvalidOperationException("Project export package was not found.");
        }

        ExportPackage? exportPackage;
        await using (var stream = File.OpenRead(exportPackagePath))
        {
            exportPackage = await JsonSerializer.DeserializeAsync(stream, OcrContractJson.ExportPackage, cancellationToken);
        }

        if (exportPackage is null)
        {
            throw new InvalidOperationException("Project export package could not be read.");
        }

        var framesDirectory = Path.Combine(extractionRoot, manifest.FramesDirectoryPath);
        var ocrDirectory = Path.Combine(extractionRoot, manifest.OcrDirectoryPath);
        var attributesDirectory = Path.Combine(extractionRoot, manifest.AttributesDirectoryPath);
        var runDirectory = Directory.GetParent(framesDirectory)?.FullName
            ?? throw new InvalidOperationException("Project frames directory is invalid.");

        var frames = exportPackage.Frames
            .Select(frame => new ExtractedFrameRecord(
                frame.FrameIndex,
                frame.TimestampMs,
                Path.Combine(framesDirectory, Path.GetFileName(frame.ImagePath))))
            .ToArray();

        var frameAnalyses = new List<FrameAnalysisResult>(frames.Length);
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var requestId = CreateRequestId(frame.FrameIndex, frame.TimestampMs);
            var responsePath = Path.Combine(ocrDirectory, $"{requestId}.response.json");
            var attributesPath = Path.Combine(attributesDirectory, $"{requestId}.attributes.json");
            if (!File.Exists(responsePath) || !File.Exists(attributesPath))
            {
                throw new InvalidOperationException($"Project bundle is missing OCR files for frame {frame.FrameIndex:D6}.");
            }

            OcrWorkerResponse? ocrResponse;
            await using (var stream = File.OpenRead(responsePath))
            {
                ocrResponse = await JsonSerializer.DeserializeAsync(stream, OcrContractJson.OcrWorkerResponse, cancellationToken);
            }

            AttributeAnalysisResult? attributeResult;
            await using (var stream = File.OpenRead(attributesPath))
            {
                attributeResult = await JsonSerializer.DeserializeAsync(stream, OcrContractJson.AttributeAnalysisResult, cancellationToken);
            }

            if (ocrResponse is null || attributeResult is null)
            {
                throw new InvalidOperationException($"Project OCR files for frame {frame.FrameIndex:D6} could not be read.");
            }

            var performance = new OcrFramePerformanceRecord(
                frame.FrameIndex,
                frame.TimestampMs,
                false,
                "project-load",
                0d,
                0d,
                0d,
                0d,
                0d,
                0d,
                0d,
                0d,
                0d);

            frameAnalyses.Add(new FrameAnalysisResult(
                frame,
                ocrResponse with { FrameIndex = frame.FrameIndex, TimestampMs = frame.TimestampMs },
                attributeResult with { FrameIndex = frame.FrameIndex, TimestampMs = frame.TimestampMs },
                performance));
        }

        var frameExtractionResult = new FrameExtractionResult(
            manifest.RunId,
            runDirectory,
            framesDirectory,
            frames);

        return new ProjectBundleLoadResult(
            projectFilePath,
            extractionRoot,
            manifest,
            exportPackage,
            frameExtractionResult,
            frameAnalyses);
    }

    public void DeleteExtractionDirectory(string? extractionDirectory)
    {
        if (string.IsNullOrWhiteSpace(extractionDirectory))
        {
            return;
        }

        TryDeleteDirectory(extractionDirectory);
    }

    private static string CreateRequestId(int frameIndex, long timestampMs)
    {
        return $"ocr-{frameIndex:D6}-{timestampMs:D8}ms";
    }

    private static UserInterfaceSettings CloneUiSettings(UserInterfaceSettings settings)
    {
        return new UserInterfaceSettings
        {
            Language = settings.Language,
            FrameIntervalSeconds = settings.FrameIntervalSeconds,
            OutputRootDirectory = settings.OutputRootDirectory,
            MainWindow = settings.MainWindow is null
                ? null
                : new MainWindowLaunchSettings
                {
                    Width = settings.MainWindow.Width,
                    Height = settings.MainWindow.Height
                }
        };
    }

    private static void TryDeleteDirectory(string? directoryPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
