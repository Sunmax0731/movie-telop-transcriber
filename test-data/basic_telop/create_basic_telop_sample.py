from __future__ import annotations

import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent
VIDEO_PATH = ROOT / "sample_basic_telop.mp4"
GROUND_TRUTH_PATH = ROOT / "ground_truth.json"
OCR_BY_TIMESTAMP_PATH = ROOT / "expected_ocr_by_timestamp.json"
FONT_PATH = Path(r"C:\Windows\Fonts\meiryo.ttc")

WIDTH = 640
HEIGHT = 360
FPS = 10
DURATION_SECONDS = 5


SEGMENTS = [
    {
        "segment_id": "seg-0001",
        "start_timestamp_ms": 1000,
        "end_timestamp_ms": 3000,
        "text": "サンプルテロップ",
        "text_type": "caption_band",
        "font_family": "Meiryo",
        "font_size": 34,
        "text_color": "#FFFFFF",
        "stroke_color": "#000000",
        "background_color": "#144A8B",
        "confidence": 0.98,
        "bounding_box": [
            {"x": 120, "y": 276},
            {"x": 520, "y": 276},
            {"x": 520, "y": 326},
            {"x": 120, "y": 326},
        ],
    },
    {
        "segment_id": "seg-0002",
        "start_timestamp_ms": 3000,
        "end_timestamp_ms": 5000,
        "text": "重要なお知らせ",
        "text_type": "title",
        "font_family": "Meiryo",
        "font_size": 38,
        "text_color": "#FFD84D",
        "stroke_color": "#202020",
        "background_color": None,
        "confidence": 0.96,
        "bounding_box": [
            {"x": 150, "y": 58},
            {"x": 490, "y": 58},
            {"x": 490, "y": 112},
            {"x": 150, "y": 112},
        ],
    },
]


def main() -> None:
    ROOT.mkdir(parents=True, exist_ok=True)
    write_video()
    write_ground_truth()
    write_ocr_by_timestamp()


def write_video() -> None:
    fourcc = cv2.VideoWriter_fourcc(*"mp4v")
    writer = cv2.VideoWriter(str(VIDEO_PATH), fourcc, FPS, (WIDTH, HEIGHT))
    if not writer.isOpened():
        raise RuntimeError(f"failed to open video writer: {VIDEO_PATH}")

    for frame_number in range(FPS * DURATION_SECONDS):
        timestamp_ms = int(frame_number / FPS * 1000)
        writer.write(render_frame(timestamp_ms))

    writer.release()


def render_frame(timestamp_ms: int) -> np.ndarray:
    image = Image.new("RGB", (WIDTH, HEIGHT), "#1A1D24")
    draw = ImageDraw.Draw(image)

    draw.rectangle((0, 0, WIDTH, HEIGHT), fill="#1A1D24")
    draw.rectangle((32, 32, WIDTH - 32, HEIGHT - 32), outline="#3A4252", width=2)
    draw.text((40, 36), f"{timestamp_ms / 1000:04.1f}s", fill="#AAB2C0", font=load_font(18))

    for segment in active_segments(timestamp_ms):
        draw_segment(draw, segment)

    return cv2.cvtColor(np.array(image), cv2.COLOR_RGB2BGR)


def active_segments(timestamp_ms: int) -> list[dict]:
    return [
        segment
        for segment in SEGMENTS
        if segment["start_timestamp_ms"] <= timestamp_ms < segment["end_timestamp_ms"]
    ]


def draw_segment(draw: ImageDraw.ImageDraw, segment: dict) -> None:
    font = load_font(segment["font_size"])
    box = segment["bounding_box"]
    left = min(point["x"] for point in box)
    top = min(point["y"] for point in box)
    right = max(point["x"] for point in box)
    bottom = max(point["y"] for point in box)

    if segment["background_color"]:
        draw.rounded_rectangle((left - 12, top - 8, right + 12, bottom + 8), radius=10, fill=segment["background_color"])

    draw.text(
        (left, top),
        segment["text"],
        font=font,
        fill=segment["text_color"],
        stroke_width=3,
        stroke_fill=segment["stroke_color"],
    )


def load_font(size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONT_PATH), size)


def write_ground_truth() -> None:
    payload = {
        "schema_version": "1.0.0",
        "video": {
            "file_name": VIDEO_PATH.name,
            "duration_ms": DURATION_SECONDS * 1000,
            "width": WIDTH,
            "height": HEIGHT,
            "fps": FPS,
        },
        "segments": [
            {
                key: value
                for key, value in segment.items()
                if key != "bounding_box"
            }
            for segment in SEGMENTS
        ],
    }
    GROUND_TRUTH_PATH.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def write_ocr_by_timestamp() -> None:
    payload = {}
    for timestamp_ms in range(0, DURATION_SECONDS * 1000 + 1, 1000):
        detections = []
        for index, segment in enumerate(active_segments(timestamp_ms), start=1):
            detections.append(
                {
                    "detection_id": f"det-{timestamp_ms:05d}-{index:02d}",
                    "text": segment["text"],
                    "confidence": segment["confidence"],
                    "bounding_box": segment["bounding_box"],
                }
            )
        payload[str(timestamp_ms)] = detections

    OCR_BY_TIMESTAMP_PATH.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
