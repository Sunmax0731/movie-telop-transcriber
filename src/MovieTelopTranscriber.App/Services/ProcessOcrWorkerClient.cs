using System.Diagnostics;
using System.Text.Json;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public sealed class ProcessOcrWorkerClient : IOcrWorkerClient
{
    private const string WorkerPathEnvironmentVariable = "MOVIE_TELOP_OCR_WORKER";
    private const string EngineEnvironmentVariable = "MOVIE_TELOP_OCR_ENGINE";

    public string EngineName =>
        Environment.GetEnvironmentVariable(EngineEnvironmentVariable)
        ?? (HasConfiguredWorker ? "external-json-worker" : "json-sidecar");

    private string? WorkerPath => Environment.GetEnvironmentVariable(WorkerPathEnvironmentVariable);

    private bool HasConfiguredWorker => !string.IsNullOrWhiteSpace(WorkerPath);

    public async Task<OcrWorkerExecutionResult> RecognizeAsync(
        OcrWorkerRequest request,
        string ocrDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ocrDirectory);

        var requestPath = Path.Combine(ocrDirectory, $"{request.RequestId}.request.json");
        var responsePath = Path.Combine(ocrDirectory, $"{request.RequestId}.response.json");

        var requestWriteStopwatch = Stopwatch.StartNew();
        await WriteJsonAsync(requestPath, request, cancellationToken);
        requestWriteStopwatch.Stop();

        var workerPath = WorkerPath;
        if (!string.IsNullOrWhiteSpace(workerPath))
        {
            var workerResponse = await RunExternalWorkerAsync(workerPath, request, requestPath, responsePath, cancellationToken);
            if (!File.Exists(responsePath))
            {
                await WriteJsonAsync(responsePath, workerResponse.Response, cancellationToken);
            }

            return workerResponse with
            {
                RequestWriteMs = requestWriteStopwatch.Elapsed.TotalMilliseconds
            };
        }

        var sidecarPath = Path.ChangeExtension(request.ImagePath, ".ocr.json");
        if (File.Exists(sidecarPath))
        {
            var responseReadStopwatch = Stopwatch.StartNew();
            var sidecarResponse = await ReadResponseAsync(sidecarPath, request, cancellationToken);
            responseReadStopwatch.Stop();
            await WriteJsonAsync(responsePath, sidecarResponse, cancellationToken);
            return new OcrWorkerExecutionResult(
                sidecarResponse,
                requestWriteStopwatch.Elapsed.TotalMilliseconds,
                0d,
                0d,
                responseReadStopwatch.Elapsed.TotalMilliseconds);
        }

        var missingSidecarResponse = CreateErrorResponse(
            request,
            "OCR_SIDECAR_NOT_FOUND",
            "No OCR worker is configured and no sidecar OCR response exists for this frame.",
            $"Set {EngineEnvironmentVariable}=paddleocr, configure {WorkerPathEnvironmentVariable}, provide movie-telop-transcriber.settings.json, or provide sidecar file: {sidecarPath}",
            true);
        await WriteJsonAsync(responsePath, missingSidecarResponse, cancellationToken);
        return new OcrWorkerExecutionResult(
            missingSidecarResponse,
            requestWriteStopwatch.Elapsed.TotalMilliseconds,
            0d,
            0d,
            0d);
    }

    private static async Task<OcrWorkerExecutionResult> RunExternalWorkerAsync(
        string workerPath,
        OcrWorkerRequest request,
        string requestPath,
        string responsePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(workerPath))
        {
            return CreateFailureResult(
                request,
                "OCR_WORKER_NOT_FOUND",
                "OCR worker executable was not found.",
                workerPath,
                true);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add(requestPath);
        startInfo.ArgumentList.Add(responsePath);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return CreateFailureResult(
                request,
                "OCR_WORKER_START_FAILED",
                "OCR worker process could not be started.",
                workerPath,
                true);
        }

        var workerExecutionStopwatch = Stopwatch.StartNew();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        workerExecutionStopwatch.Stop();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            return CreateFailureResult(
                request,
                "OCR_PROCESS_FAILED",
                "OCR worker returned a non-zero exit code.",
                $"exit_code={process.ExitCode}; stdout={stdout}; stderr={stderr}",
                true,
                workerExecutionStopwatch.Elapsed.TotalMilliseconds);
        }

        if (!File.Exists(responsePath))
        {
            return CreateFailureResult(
                request,
                "OCR_RESPONSE_NOT_FOUND",
                "OCR worker completed without writing a response file.",
                responsePath,
                true,
                workerExecutionStopwatch.Elapsed.TotalMilliseconds);
        }

        var responseReadStopwatch = Stopwatch.StartNew();
        var response = await ReadResponseAsync(responsePath, request, cancellationToken);
        responseReadStopwatch.Stop();
        return new OcrWorkerExecutionResult(
            response,
            0d,
            0d,
            workerExecutionStopwatch.Elapsed.TotalMilliseconds,
            responseReadStopwatch.Elapsed.TotalMilliseconds);
    }

    private static OcrWorkerExecutionResult CreateFailureResult(
        OcrWorkerRequest request,
        string code,
        string message,
        string? details,
        bool recoverable,
        double workerExecutionMs = 0d)
    {
        return new OcrWorkerExecutionResult(
            CreateErrorResponse(request, code, message, details, recoverable),
            0d,
            0d,
            workerExecutionMs,
            0d);
    }

    private static async Task<OcrWorkerResponse> ReadResponseAsync(
        string responsePath,
        OcrWorkerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(responsePath);
            var response = await JsonSerializer.DeserializeAsync(
                stream,
                OcrContractJson.OcrWorkerResponse,
                cancellationToken);

            if (response is null)
            {
                return CreateErrorResponse(
                    request,
                    "OCR_RESPONSE_INVALID",
                    "OCR worker response was empty.",
                    responsePath,
                    true);
            }

            return response;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return CreateErrorResponse(
                request,
                "OCR_RESPONSE_READ_FAILED",
                "OCR worker response could not be read.",
                ex.Message,
                true);
        }
    }

    private static async Task WriteJsonAsync(string path, OcrWorkerRequest value, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, OcrContractJson.OcrWorkerRequest, cancellationToken);
    }

    private static async Task WriteJsonAsync(string path, OcrWorkerResponse value, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, OcrContractJson.OcrWorkerResponse, cancellationToken);
    }

    private static OcrWorkerResponse CreateErrorResponse(
        OcrWorkerRequest request,
        string code,
        string message,
        string? details,
        bool recoverable)
    {
        return new OcrWorkerResponse(
            request.RequestId,
            "error",
            request.FrameIndex,
            request.TimestampMs,
            Array.Empty<OcrDetectionRecord>(),
            new ProcessingError(code, message, details, recoverable));
    }
}
