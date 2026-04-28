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
        result = ocr.predict(image_path)
        payload = extract_result_payload(result)
        detections = self._create_detections(request, payload)

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
            self._models[lang] = self._paddle_ocr_type(
                lang=lang,
                ocr_version=self._version,
                device=self._device,
                use_doc_orientation_classify=False,
                use_doc_unwarping=False,
                use_textline_orientation=False,
                text_rec_score_thresh=0.0,
            )

        return self._models[lang]

    def _create_detections(self, request: dict[str, Any], payload: dict[str, Any]) -> list[dict[str, Any]]:
        texts = payload.get("rec_texts") or []
        scores = payload.get("rec_scores") or []
        polys = payload.get("rec_polys") or payload.get("dt_polys") or []
        frame_index = int(request.get("frame_index", 0))
        timestamp_ms = int(request.get("timestamp_ms", 0))
        detections: list[dict[str, Any]] = []

        for index, text in enumerate(texts):
            normalized_text = str(text).strip()
            if not normalized_text:
                continue

            confidence = to_float(scores[index]) if index < len(scores) else None
            if confidence is not None and confidence < self._min_score:
                continue

            polygon = polys[index] if index < len(polys) else []
            detections.append(
                {
                    "detection_id": f"paddleocr-{frame_index:06d}-{timestamp_ms:08d}ms-{index + 1:02d}",
                    "text": normalized_text,
                    "confidence": confidence,
                    "bounding_box": to_bounding_points(polygon),
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


def to_bounding_points(polygon: Any) -> list[dict[str, float]]:
    points = to_plain_list(polygon)
    if not points:
        return []

    bounding_points: list[dict[str, float]] = []
    for point in points:
        if len(point) < 2:
            continue
        bounding_points.append({"x": to_float(point[0]) or 0.0, "y": to_float(point[1]) or 0.0})

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
