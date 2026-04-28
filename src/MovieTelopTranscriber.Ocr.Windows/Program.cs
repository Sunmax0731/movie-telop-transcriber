using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

return await WindowsOcrWorker.RunAsync(args);

internal static class WindowsOcrWorker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: MovieTelopTranscriber.Ocr.Windows <request.json> <response.json>");
            return 2;
        }

        var requestPath = args[0];
        var responsePath = args[1];

        OcrWorkerRequest? request = null;
        try
        {
            await using var requestStream = File.OpenRead(requestPath);
            request = await JsonSerializer.DeserializeAsync<OcrWorkerRequest>(requestStream, JsonOptions);
            if (request is null)
            {
                Console.Error.WriteLine("request json was empty.");
                return 2;
            }

            var response = await RecognizeAsync(request);
            await WriteResponseAsync(responsePath, response);
            return 0;
        }
        catch (Exception ex) when (request is not null)
        {
            var response = CreateErrorResponse(
                request,
                "WINDOWS_OCR_FAILED",
                "Windows OCR worker failed.",
                ex.Message,
                true);
            await WriteResponseAsync(responsePath, response);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static async Task<OcrWorkerResponse> RecognizeAsync(OcrWorkerRequest request)
    {
        if (!File.Exists(request.ImagePath))
        {
            return CreateErrorResponse(
                request,
                "OCR_IMAGE_NOT_FOUND",
                "OCR target image was not found.",
                request.ImagePath,
                true);
        }

        var engine = CreateEngine(request.LanguageHint);
        if (engine is null)
        {
            return CreateErrorResponse(
                request,
                "OCR_ENGINE_NOT_AVAILABLE",
                "Windows OCR engine is not available for the requested language.",
                $"language_hint={request.LanguageHint}",
                true);
        }

        using var image = await LoadBitmapAsync(request.ImagePath);
        var result = await engine.RecognizeAsync(image.Bitmap);
        var detections = result.Lines
            .Select((line, index) => CreateDetection(request, line, index, image.Scale))
            .Where(detection => !string.IsNullOrWhiteSpace(detection.Text))
            .Where(detection => EstimateHeight(detection.BoundingBox) >= GetMinimumLineHeight())
            .ToArray();

        return new OcrWorkerResponse(
            request.RequestId,
            "success",
            request.FrameIndex,
            request.TimestampMs,
            detections,
            null);
    }

    private static OcrEngine? CreateEngine(string languageHint)
    {
        foreach (var languageTag in GetLanguageCandidates(languageHint))
        {
            try
            {
                var engine = OcrEngine.TryCreateFromLanguage(new Language(languageTag));
                if (engine is not null)
                {
                    return engine;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    private static IEnumerable<string> GetLanguageCandidates(string languageHint)
    {
        var normalized = languageHint.Trim().ToLowerInvariant();
        if (normalized.StartsWith("ja", StringComparison.Ordinal))
        {
            yield return "ja";
            yield break;
        }

        if (normalized.StartsWith("en", StringComparison.Ordinal))
        {
            yield return "en";
            yield break;
        }

        if (normalized.StartsWith("zh", StringComparison.Ordinal))
        {
            yield return "zh-Hans";
            yield return "zh-Hant";
            yield return "zh-CN";
            yield return "zh-TW";
            yield break;
        }

        if (normalized.StartsWith("ko", StringComparison.Ordinal))
        {
            yield return "ko";
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(languageHint))
        {
            yield return languageHint;
        }
    }

    private static async Task<OcrInputImage> LoadBitmapAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var scale = Math.Min(2d, (double)OcrEngine.MaxImageDimension / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale))
        };

        var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        return new OcrInputImage(bitmap, scale);
    }

    private static OcrDetectionRecord CreateDetection(OcrWorkerRequest request, OcrLine line, int index, double scale)
    {
        var bounds = line.Words
            .Select(word => word.BoundingRect)
            .ToArray();
        var boundingBox = bounds.Length == 0
            ? Array.Empty<OcrBoundingPoint>()
            : CreateBoundingBox(
                bounds.Min(rect => rect.X) / scale,
                bounds.Min(rect => rect.Y) / scale,
                bounds.Max(rect => rect.X + rect.Width) / scale,
                bounds.Max(rect => rect.Y + rect.Height) / scale);

        return new OcrDetectionRecord(
            $"winocr-{request.FrameIndex:D6}-{request.TimestampMs:D8}ms-{index + 1:D2}",
            NormalizeText(line.Text),
            null,
            boundingBox);
    }

    private static double GetMinimumLineHeight()
    {
        var value = Environment.GetEnvironmentVariable("MOVIE_TELOP_WINDOWS_OCR_MIN_HEIGHT");
        return double.TryParse(value, out var minHeight) ? minHeight : 18d;
    }

    private static double EstimateHeight(IReadOnlyList<OcrBoundingPoint> boundingBox)
    {
        if (boundingBox.Count == 0)
        {
            return 0d;
        }

        return boundingBox.Max(point => point.Y) - boundingBox.Min(point => point.Y);
    }

    private static string NormalizeText(string text)
    {
        return ContainsJapaneseOrCjk(text)
            ? string.Concat(text.Where(character => !char.IsWhiteSpace(character)))
            : text.Trim();
    }

    private static bool ContainsJapaneseOrCjk(string text)
    {
        return text.Any(character =>
            character is >= '\u3040' and <= '\u30ff'
            || character is >= '\u3400' and <= '\u9fff'
            || character is >= '\uf900' and <= '\ufaff');
    }

    private static OcrBoundingPoint[] CreateBoundingBox(double left, double top, double right, double bottom)
    {
        return
        [
            new OcrBoundingPoint(left, top),
            new OcrBoundingPoint(right, top),
            new OcrBoundingPoint(right, bottom),
            new OcrBoundingPoint(left, bottom)
        ];
    }

    private static async Task WriteResponseAsync(string responsePath, OcrWorkerResponse response)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(responsePath))!);
        await using var responseStream = File.Create(responsePath);
        await JsonSerializer.SerializeAsync(responseStream, response, JsonOptions);
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

internal sealed record OcrWorkerRequest(
    string RequestId,
    int FrameIndex,
    long TimestampMs,
    string ImagePath,
    string LanguageHint,
    string Engine);

internal sealed record OcrWorkerResponse(
    string RequestId,
    string Status,
    int FrameIndex,
    long TimestampMs,
    IReadOnlyList<OcrDetectionRecord> Detections,
    ProcessingError? Error);

internal sealed record OcrDetectionRecord(
    string DetectionId,
    string Text,
    double? Confidence,
    IReadOnlyList<OcrBoundingPoint> BoundingBox);

internal sealed record OcrBoundingPoint(double X, double Y);

internal sealed record ProcessingError(
    string Code,
    string Message,
    string? Details,
    bool Recoverable);

internal sealed class OcrInputImage(SoftwareBitmap bitmap, double scale) : IDisposable
{
    public SoftwareBitmap Bitmap { get; } = bitmap;

    public double Scale { get; } = scale;

    public void Dispose()
    {
        Bitmap.Dispose();
    }
}
