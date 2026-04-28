#!/usr/bin/env python
"""PaddleOCR worker for Movie Telop Transcriber.

The worker supports two modes:
- stdio mode: keeps PaddleOCR models loaded and handles JSON line commands.
- single-shot mode: reads one request JSON and writes one response JSON.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import tempfile
import traceback
from pathlib import Path
from typing import Any


PROTOCOL_STDOUT = sys.stdout
sys.stdout = sys.stderr


def main() -> int:
    parser = argparse.ArgumentParser(description="Movie Telop Transcriber PaddleOCR worker")
    parser.add_argument("request_json", nargs="?", help="OCR request JSON path")
    parser.add_argument("response_json", nargs="?", help="OCR response JSON path")
    parser.add_argument("--stdio", action="store_true", help="run as a persistent JSON-lines worker")
    args = parser.parse_args()

    worker = PaddleOcrWorker()
    if args.stdio:
        run_stdio(worker)
        return 0

    if not args.request_json or not args.response_json:
        print("usage: paddle_ocr_worker.py <request.json> <response.json>", file=sys.stderr)
        return 2

    worker.process_file(args.request_json, args.response_json)
    return 0


def run_stdio(worker: "PaddleOcrWorker") -> None:
    for line in sys.stdin:
        if not line.strip():
            continue

        try:
            command = json.loads(line)
            request_path = command["request_path"]
            response_path = command["response_path"]
            worker.process_file(request_path, response_path)
            write_ack({"status": "ok", "message": None, "response_path": response_path})
        except Exception as exc:  # noqa: BLE001 - worker must keep serving later frames.
            print(traceback.format_exc(), file=sys.stderr)
            write_ack({"status": "error", "message": str(exc), "response_path": None})


def write_ack(payload: dict[str, Any]) -> None:
    print(json.dumps(payload, ensure_ascii=False), file=PROTOCOL_STDOUT, flush=True)


class PaddleOcrWorker:
    def __init__(self) -> None:
        from paddleocr import PaddleOCR

        self._paddle_ocr_type = PaddleOCR
        self._models: dict[str, Any] = {}
        self._version = os.environ.get("MOVIE_TELOP_PADDLEOCR_VERSION", "PP-OCRv5")
        self._device = os.environ.get("MOVIE_TELOP_PADDLEOCR_DEVICE", "cpu")
        self._min_score = float(os.environ.get("MOVIE_TELOP_PADDLEOCR_MIN_SCORE", "0.5"))
        self._normalize_small_kana = parse_bool(
            os.environ.get("MOVIE_TELOP_PADDLEOCR_NORMALIZE_SMALL_KANA"),
            True,
        )
        self._preprocess = parse_bool(os.environ.get("MOVIE_TELOP_PADDLEOCR_PREPROCESS"), True)
        self._preprocess_upscale = clamp_float(
            parse_float(os.environ.get("MOVIE_TELOP_PADDLEOCR_UPSCALE"), 1.0),
            1.0,
            4.0,
        )
        self._preprocess_contrast = clamp_float(
            parse_float(os.environ.get("MOVIE_TELOP_PADDLEOCR_CONTRAST"), 1.1),
            0.1,
            4.0,
        )
        self._preprocess_sharpen = parse_bool(os.environ.get("MOVIE_TELOP_PADDLEOCR_SHARPEN"), True)
        self._text_det_thresh = parse_float(os.environ.get("MOVIE_TELOP_PADDLEOCR_TEXT_DET_THRESH"), None)
        self._text_det_box_thresh = parse_float(os.environ.get("MOVIE_TELOP_PADDLEOCR_TEXT_DET_BOX_THRESH"), None)
        self._text_det_unclip_ratio = parse_float(
            os.environ.get("MOVIE_TELOP_PADDLEOCR_TEXT_DET_UNCLIP_RATIO"),
            None,
        )
        self._text_det_limit_side_len = parse_int(
            os.environ.get("MOVIE_TELOP_PADDLEOCR_TEXT_DET_LIMIT_SIDE_LEN"),
            None,
        )
        self._use_textline_orientation = parse_bool(
            os.environ.get("MOVIE_TELOP_PADDLEOCR_USE_TEXTLINE_ORIENTATION"),
            False,
        )
        self._use_doc_unwarping = parse_bool(
            os.environ.get("MOVIE_TELOP_PADDLEOCR_USE_DOC_UNWARPING"),
            False,
        )

    def process_file(self, request_path: str, response_path: str) -> None:
        try:
            request = read_json(request_path)
            response = self.recognize(request)
        except Exception as exc:  # noqa: BLE001 - response contract carries recoverable errors.
            try:
                request = read_json(request_path) if Path(request_path).exists() else {}
            except Exception:  # noqa: BLE001 - fall back to a contract-shaped error.
                request = {}
            response = create_error_response(
                request,
                "PADDLEOCR_FAILED",
                "PaddleOCR worker failed.",
                str(exc),
                True,
            )
            print(traceback.format_exc(), file=sys.stderr)

        write_json(response_path, response)

    def recognize(self, request: dict[str, Any]) -> dict[str, Any]:
        image_path = request.get("image_path", "")
        if not Path(image_path).exists():
            return create_error_response(
                request,
                "OCR_IMAGE_NOT_FOUND",
                "OCR target image was not found.",
                image_path,
                True,
            )

        lang = resolve_language(request.get("language_hint", ""))
        ocr = self._get_model(lang)
        ocr_image_path, coordinate_scale, temporary_image_path = self._prepare_image_for_ocr(image_path)
        try:
            result = ocr.predict(ocr_image_path)
            payload = extract_result_payload(result)
            detections = self._create_detections(request, payload, lang, coordinate_scale)
        finally:
            if temporary_image_path:
                try:
                    Path(temporary_image_path).unlink(missing_ok=True)
                except OSError:
                    pass

        return {
            "request_id": request.get("request_id", ""),
            "status": "success",
            "frame_index": int(request.get("frame_index", 0)),
            "timestamp_ms": int(request.get("timestamp_ms", 0)),
            "detections": detections,
            "error": None,
        }

    def _get_model(self, lang: str) -> Any:
        if lang not in self._models:
            kwargs = {
                "lang": lang,
                "ocr_version": self._version,
                "device": self._device,
                "use_doc_orientation_classify": False,
                "use_doc_unwarping": self._use_doc_unwarping,
                "use_textline_orientation": self._use_textline_orientation,
                "text_rec_score_thresh": 0.0,
            }
            if self._text_det_thresh is not None:
                kwargs["text_det_thresh"] = self._text_det_thresh
            if self._text_det_box_thresh is not None:
                kwargs["text_det_box_thresh"] = self._text_det_box_thresh
            if self._text_det_unclip_ratio is not None:
                kwargs["text_det_unclip_ratio"] = self._text_det_unclip_ratio
            if self._text_det_limit_side_len is not None:
                kwargs["text_det_limit_side_len"] = self._text_det_limit_side_len

            self._models[lang] = self._paddle_ocr_type(**kwargs)

        return self._models[lang]

    def _prepare_image_for_ocr(self, image_path: str) -> tuple[str, float, str | None]:
        if not self._preprocess:
            return image_path, 1.0, None

        should_upscale = self._preprocess_upscale > 1.0
        should_adjust_contrast = abs(self._preprocess_contrast - 1.0) > 0.001
        if not should_upscale and not should_adjust_contrast and not self._preprocess_sharpen:
            return image_path, 1.0, None

        try:
            from PIL import Image, ImageEnhance, ImageFilter

            image = Image.open(image_path).convert("RGB")
            if should_upscale:
                resampling = getattr(getattr(Image, "Resampling", Image), "LANCZOS")
                width = max(1, int(round(image.width * self._preprocess_upscale)))
                height = max(1, int(round(image.height * self._preprocess_upscale)))
                image = image.resize((width, height), resampling)

            if should_adjust_contrast:
                image = ImageEnhance.Contrast(image).enhance(self._preprocess_contrast)

            if self._preprocess_sharpen:
                image = image.filter(ImageFilter.UnsharpMask(radius=1.2, percent=140, threshold=3))

            fd, temporary_path = tempfile.mkstemp(prefix="movie_telop_ocr_", suffix=".png")
            os.close(fd)
            image.save(temporary_path)
            return temporary_path, self._preprocess_upscale if should_upscale else 1.0, temporary_path
        except Exception as exc:  # noqa: BLE001 - fall back to original frames when preprocessing fails.
            print(f"PaddleOCR preprocessing skipped: {exc}", file=sys.stderr)
            return image_path, 1.0, None

    def _create_detections(
        self,
        request: dict[str, Any],
        payload: dict[str, Any],
        lang: str,
        coordinate_scale: float,
    ) -> list[dict[str, Any]]:
        texts = payload.get("rec_texts") or []
        scores = payload.get("rec_scores") or []
        polys = payload.get("rec_polys") or payload.get("dt_polys") or []
        frame_index = int(request.get("frame_index", 0))
        timestamp_ms = int(request.get("timestamp_ms", 0))
        detections: list[dict[str, Any]] = []

        for index, text in enumerate(texts):
            polygon = polys[index] if index < len(polys) else []
            normalized_text = str(text).strip()
            if self._normalize_small_kana and lang == "japan":
                normalized_text = normalize_japanese_small_kana(normalized_text)
            normalized_text = normalize_symbol_text(normalized_text, polygon)

            if not normalized_text:
                continue

            confidence = to_float(scores[index]) if index < len(scores) else None
            if confidence is not None and confidence < self._min_score and not is_symbol_candidate(normalized_text):
                continue

            detections.append(
                {
                    "detection_id": f"paddleocr-{frame_index:06d}-{timestamp_ms:08d}ms-{index + 1:02d}",
                    "text": normalized_text,
                    "confidence": confidence,
                    "bounding_box": to_bounding_points(polygon, coordinate_scale),
                }
            )

        return detections


def resolve_language(language_hint: str) -> str:
    configured = os.environ.get("MOVIE_TELOP_PADDLEOCR_LANG")
    if configured:
        return configured

    normalized = (language_hint or "").strip().lower()
    if normalized.startswith("ja"):
        return "japan"
    if normalized.startswith("ko"):
        return "korean"
    if normalized.startswith("zh"):
        return "ch"
    if normalized.startswith("en"):
        return "en"
    return "japan"


def parse_bool(value: str | None, default: bool) -> bool:
    if value is None:
        return default

    normalized = value.strip().lower()
    if normalized in {"1", "true", "yes", "on"}:
        return True
    if normalized in {"0", "false", "no", "off"}:
        return False
    return default


def parse_float(value: str | None, default: float | None) -> float | None:
    if value is None or not value.strip():
        return default

    try:
        return float(value)
    except ValueError:
        return default


def parse_int(value: str | None, default: int | None) -> int | None:
    if value is None or not value.strip():
        return default

    try:
        return int(value)
    except ValueError:
        return default


def clamp_float(value: float | None, minimum: float, maximum: float) -> float:
    if value is None:
        return minimum

    return max(minimum, min(maximum, value))


def normalize_japanese_small_kana(text: str) -> str:
    """Correct common OCR substitutions where small kana are read as full-size kana.

    The rules are intentionally conservative: they only handle yoon after i-row
    kana and sokuon before t/d-row kana, plus a few high-frequency lexical cases.
    """

    text = replace_yoon_kana(text)
    text = replace_sokuon_kana(text)
    return replace_common_small_kana_words(text)


def replace_yoon_kana(text: str) -> str:
    yoon_sources = "きぎしじちぢにひびぴみりキギシジチヂニヒビピミリ"
    small_map = {
        "や": "ゃ",
        "ゆ": "ゅ",
        "よ": "ょ",
        "ヤ": "ャ",
        "ユ": "ュ",
        "ヨ": "ョ",
    }
    output: list[str] = []
    for index, char in enumerate(text):
        previous = text[index - 1] if index > 0 else ""
        output.append(small_map[char] if previous in yoon_sources and char in small_map else char)
    return "".join(output)


def replace_sokuon_kana(text: str) -> str:
    sokuon_next = set("たちつてとだぢづでどタチツテトダヂヅデド")
    output: list[str] = []
    for index, char in enumerate(text):
        next_char = text[index + 1] if index + 1 < len(text) else ""
        if char == "つ" and next_char in sokuon_next:
            output.append("っ")
        elif char == "ツ" and next_char in sokuon_next:
            output.append("ッ")
        else:
            output.append(char)
    return "".join(output)


def replace_common_small_kana_words(text: str) -> str:
    replacements = {
        "さつき": "さっき",
        "びつくり": "びっくり",
        "ビツクリ": "ビックリ",
        "ゆつくり": "ゆっくり",
        "ユツクリ": "ユックリ",
    }
    for source, replacement in replacements.items():
        text = text.replace(source, replacement)
    return text


def normalize_symbol_text(text: str, polygon: Any) -> str:
    """Preserve common telop symbols that Japanese OCR may read as plain circles."""

    if text in {"◎", "〇", "○", "◯"}:
        return "◎"

    if text in {"O", "o", "0"} and is_near_square_polygon(polygon):
        return "◎"

    return text


def is_symbol_candidate(text: str) -> bool:
    return text in {"◎", "〇", "○", "◯"}


def is_near_square_polygon(polygon: Any) -> bool:
    try:
        points = list(polygon)
        xs = [float(point[0]) for point in points]
        ys = [float(point[1]) for point in points]
    except Exception:  # noqa: BLE001 - symbol normalization must never fail OCR.
        return False

    if not xs or not ys:
        return False

    width = max(xs) - min(xs)
    height = max(ys) - min(ys)
    if width <= 0 or height <= 0:
        return False

    ratio = width / height
    return 0.65 <= ratio <= 1.55


def extract_result_payload(result: Any) -> dict[str, Any]:
    first = result[0] if isinstance(result, list) and result else result
    if hasattr(first, "json"):
        payload = first.json
    elif isinstance(first, dict):
        payload = first
    else:
        payload = dict(first)

    if "res" in payload and isinstance(payload["res"], dict):
        return payload["res"]

    return payload


def to_bounding_points(polygon: Any, coordinate_scale: float = 1.0) -> list[dict[str, float]]:
    points = to_plain_list(polygon)
    if not points:
        return []

    bounding_points: list[dict[str, float]] = []
    scale = coordinate_scale if coordinate_scale > 0 else 1.0
    for point in points:
        if len(point) < 2:
            continue
        x = (to_float(point[0]) or 0.0) / scale
        y = (to_float(point[1]) or 0.0) / scale
        bounding_points.append({"x": x, "y": y})

    return bounding_points


def to_plain_list(value: Any) -> Any:
    if hasattr(value, "tolist"):
        return value.tolist()
    if isinstance(value, tuple):
        return [to_plain_list(item) for item in value]
    if isinstance(value, list):
        return [to_plain_list(item) for item in value]
    return value


def to_float(value: Any) -> float | None:
    if value is None:
        return None
    if hasattr(value, "item"):
        value = value.item()
    return float(value)


def read_json(path: str) -> dict[str, Any]:
    with open(path, "r", encoding="utf-8") as stream:
        return json.load(stream)


def write_json(path: str, payload: dict[str, Any]) -> None:
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8") as stream:
        json.dump(payload, stream, ensure_ascii=False, indent=2)


def create_error_response(
    request: dict[str, Any],
    code: str,
    message: str,
    details: str | None,
    recoverable: bool,
) -> dict[str, Any]:
    return {
        "request_id": request.get("request_id", ""),
        "status": "error",
        "frame_index": int(request.get("frame_index", 0)),
        "timestamp_ms": int(request.get("timestamp_ms", 0)),
        "detections": [],
        "error": {
            "code": code,
            "message": message,
            "details": details,
            "recoverable": recoverable,
        },
    }


if __name__ == "__main__":
    raise SystemExit(main())
