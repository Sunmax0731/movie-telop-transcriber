using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public sealed class PaddleOcrWorkerClient : IOcrWorkerClient, IAsyncDisposable, IDisposable
{
    private const string PythonEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_PYTHON";
    private const string ScriptEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_SCRIPT";
    private const int MaxStoredErrorLines = 40;
    private static readonly string[] WorkerSettingsEnvironmentVariables =
    [
        PythonEnvironmentVariable,
        ScriptEnvironmentVariable,
        "MOVIE_TELOP_PADDLEOCR_VERSION",
        "MOVIE_TELOP_PADDLEOCR_DEVICE",
        "MOVIE_TELOP_PADDLEOCR_LANG",
        "MOVIE_TELOP_PADDLEOCR_MIN_SCORE",
        "MOVIE_TELOP_PADDLEOCR_NORMALIZE_SMALL_KANA",
        "MOVIE_TELOP_PADDLEOCR_PREPROCESS",
        "MOVIE_TELOP_PADDLEOCR_UPSCALE",
        "MOVIE_TELOP_PADDLEOCR_CONTRAST",
        "MOVIE_TELOP_PADDLEOCR_SHARPEN",
        "MOVIE_TELOP_PADDLEOCR_TEXT_DET_THRESH",
        "MOVIE_TELOP_PADDLEOCR_TEXT_DET_BOX_THRESH",
        "MOVIE_TELOP_PADDLEOCR_TEXT_DET_UNCLIP_RATIO",
        "MOVIE_TELOP_PADDLEOCR_TEXT_DET_LIMIT_SIDE_LEN",
        "MOVIE_TELOP_PADDLEOCR_USE_TEXTLINE_ORIENTATION",
        "MOVIE_TELOP_PADDLEOCR_USE_DOC_UNWARPING"
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentQueue<string> _stderrLines = new();

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private Task? _stderrPump;
    private string? _workerSettingsSignature;

    public string EngineName => "paddleocr";

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

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var initializationStopwatch = Stopwatch.StartNew();
            var startFailure = EnsureWorkerStarted(request);
            initializationStopwatch.Stop();
            if (startFailure is not null)
            {
                await WriteJsonAsync(responsePath, startFailure.Response, cancellationToken);
                return startFailure with
                {
                    RequestWriteMs = requestWriteStopwatch.Elapsed.TotalMilliseconds,
                    WorkerInitializationMs = initializationStopwatch.Elapsed.TotalMilliseconds
                };
            }

            var commandJson = CreateCommandJson(requestPath, responsePath);
            var workerExecutionStopwatch = Stopwatch.StartNew();
            await _stdin!.WriteLineAsync(commandJson);
            await _stdin.FlushAsync();

            var ack = await ReadAckAsync(cancellationToken);
            workerExecutionStopwatch.Stop();
            if (ack is null)
            {
                ResetWorker();
                var response = CreateFailureResult(
                    request,
                    "PADDLEOCR_WORKER_STOPPED",
                    "PaddleOCR worker stopped before returning a response.",
                    GetRecentErrorOutput(),
                    true,
                    workerExecutionStopwatch.Elapsed.TotalMilliseconds);
                await WriteJsonAsync(responsePath, response.Response, cancellationToken);
                return response;
            }

            if (!string.Equals(ack.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(responsePath))
                {
                    var failedResponseReadStopwatch = Stopwatch.StartNew();
                    var failedResponse = await ReadResponseAsync(responsePath, request, cancellationToken);
                    failedResponseReadStopwatch.Stop();
                    return new OcrWorkerExecutionResult(
                        failedResponse,
                        requestWriteStopwatch.Elapsed.TotalMilliseconds,
                        initializationStopwatch.Elapsed.TotalMilliseconds,
                        workerExecutionStopwatch.Elapsed.TotalMilliseconds,
                        failedResponseReadStopwatch.Elapsed.TotalMilliseconds);
                }

                var response = CreateFailureResult(
                    request,
                    "PADDLEOCR_WORKER_FAILED",
                    "PaddleOCR worker returned an error.",
                    ack?.Message ?? GetRecentErrorOutput(),
                    true,
                    workerExecutionStopwatch.Elapsed.TotalMilliseconds);
                await WriteJsonAsync(responsePath, response.Response, cancellationToken);
                return response;
            }

            if (!File.Exists(responsePath))
            {
                var response = CreateFailureResult(
                    request,
                    "PADDLEOCR_RESPONSE_NOT_FOUND",
                    "PaddleOCR worker completed without writing a response file.",
                    responsePath,
                    true,
                    workerExecutionStopwatch.Elapsed.TotalMilliseconds);
                await WriteJsonAsync(responsePath, response.Response, cancellationToken);
                return response;
            }

            var responseReadStopwatch = Stopwatch.StartNew();
            var responseValue = await ReadResponseAsync(responsePath, request, cancellationToken);
            responseReadStopwatch.Stop();
            return new OcrWorkerExecutionResult(
                responseValue,
                requestWriteStopwatch.Elapsed.TotalMilliseconds,
                initializationStopwatch.Elapsed.TotalMilliseconds,
                workerExecutionStopwatch.Elapsed.TotalMilliseconds,
                responseReadStopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string CreateCommandJson(string requestPath, string responsePath)
    {
        return "{\"request_path\":"
            + ToJsonString(requestPath)
            + ",\"response_path\":"
            + ToJsonString(responsePath)
            + "}";
    }

    private static string ToJsonString(string value)
    {
        return "\""
            + value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
            + "\"";
    }

    private async Task<PaddleOcrWorkerAck?> ReadAckAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await _stdout!.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var ack = JsonSerializer.Deserialize(line, OcrContractJson.PaddleOcrWorkerAck);
                if (ack is not null && !string.IsNullOrWhiteSpace(ack.Status))
                {
                    return ack;
                }
            }
            catch (JsonException)
            {
                _stderrLines.Enqueue(line);
                while (_stderrLines.Count > MaxStoredErrorLines && _stderrLines.TryDequeue(out _))
                {
                }
            }
        }
    }

    private OcrWorkerExecutionResult? EnsureWorkerStarted(OcrWorkerRequest request)
    {
        var settingsSignature = CreateWorkerSettingsSignature();
        if (_process is { HasExited: false } && _stdin is not null && _stdout is not null)
        {
            if (string.Equals(_workerSettingsSignature, settingsSignature, StringComparison.Ordinal))
            {
                return null;
            }

            ResetWorker();
        }

        ResetWorker();

        var scriptPath = ResolveScriptPath();
        if (scriptPath is null)
        {
            return ToFailureResult(CreateErrorResponse(
                request,
                "PADDLEOCR_SCRIPT_NOT_FOUND",
                "PaddleOCR worker script was not found.",
                $"Set {ScriptEnvironmentVariable} or copy tools/ocr/paddle_ocr_worker.py next to the app.",
                true));
        }

        var pythonPath = Environment.GetEnvironmentVariable(PythonEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(pythonPath))
        {
            pythonPath = "python";
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("--stdio");
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.Environment["PYTHONUTF8"] = "1";

            _process = Process.Start(startInfo);
            if (_process is null)
            {
                return ToFailureResult(CreateErrorResponse(
                    request,
                    "PADDLEOCR_WORKER_START_FAILED",
                    "PaddleOCR worker process could not be started.",
                    pythonPath,
                    true));
            }

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;
            _stderrPump = Task.Run(async () => await PumpStandardErrorAsync(_process));
            _workerSettingsSignature = settingsSignature;
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            ResetWorker();
            return ToFailureResult(CreateErrorResponse(
                request,
                "PADDLEOCR_WORKER_START_FAILED",
                "PaddleOCR worker process could not be started.",
                $"{pythonPath}: {ex.Message}",
                true));
        }
    }

    private static string CreateWorkerSettingsSignature()
    {
        return string.Join(
            "\n",
            WorkerSettingsEnvironmentVariables.Select(
                name => $"{name}={Environment.GetEnvironmentVariable(name) ?? string.Empty}"));
    }

    private static string? ResolveScriptPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(ScriptEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var outputPath = Path.Combine(baseDirectory, "tools", "ocr", "paddle_ocr_worker.py");
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var current = new DirectoryInfo(baseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tools", "ocr", "paddle_ocr_worker.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private async Task PumpStandardErrorAsync(Process process)
    {
        try
        {
            while (!process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                _stderrLines.Enqueue(line);
                while (_stderrLines.Count > MaxStoredErrorLines && _stderrLines.TryDequeue(out _))
                {
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private string GetRecentErrorOutput()
    {
        return string.Join(Environment.NewLine, _stderrLines.ToArray());
    }

    private void ResetWorker()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        _stdin?.Dispose();
        _stdout?.Dispose();
        _process?.Dispose();
        _stdin = null;
        _stdout = null;
        _process = null;
        _stderrPump = null;
        _workerSettingsSignature = null;
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
                    "PADDLEOCR_RESPONSE_INVALID",
                    "PaddleOCR worker response was empty.",
                    responsePath,
                    true);
            }

            return response;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return CreateErrorResponse(
                request,
                "PADDLEOCR_RESPONSE_READ_FAILED",
                "PaddleOCR worker response could not be read.",
                ex.Message,
                true);
        }
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

    private static OcrWorkerExecutionResult ToFailureResult(OcrWorkerResponse response)
    {
        return new OcrWorkerExecutionResult(response, 0d, 0d, 0d, 0d);
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

    public async ValueTask DisposeAsync()
    {
        ResetWorker();
        if (_stderrPump is not null)
        {
            await _stderrPump.ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    public void Dispose()
    {
        ResetWorker();
        _gate.Dispose();
    }
}

internal sealed record PaddleOcrWorkerAck(string Status, string? Message, string? ResponsePath);
