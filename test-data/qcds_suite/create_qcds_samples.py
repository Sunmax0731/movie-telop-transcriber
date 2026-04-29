from __future__ import annotations

import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent
FONT_PATH = Path(r"C:\Windows\Fonts\meiryo.ttc")
WIDTH = 640
HEIGHT = 360
FPS = 10


def box(left: int, top: int, right: int, bottom: int) -> list[dict[str, int]]:
    return [
        {"x": left, "y": top},
        {"x": right, "y": top},
        {"x": right, "y": bottom},
        {"x": left, "y": bottom},
    ]


SAMPLES = [
    {
        "sample_id": "low_contrast_telop",
        "file_name": "sample_low_contrast_telop.mp4",
        "duration_ms": 5000,
        "recommended_frame_interval_seconds": 1.0,
        "description": "背景と文字色が近い低コントラストのテロップを含む。",
        "segments": [
            {
                "segment_id": "seg-0001",
                "start_timestamp_ms": 1000,
                "end_timestamp_ms": 3000,
                "text": "背景になじむ字幕",
                "text_type": "caption_band",
                "font_family": "Meiryo",
                "font_size": 32,
                "text_color": "#D8DEE8",
                "stroke_color": "#8D96A3",
                "background_color": "#6E7787",
                "confidence": 0.95,
                "bounding_box": box(110, 278, 520, 324),
            },
            {
                "segment_id": "seg-0002",
                "start_timestamp_ms": 3000,
                "end_timestamp_ms": 5000,
                "text": "低コントラスト検証",
                "text_type": "caption_band",
                "font_family": "Meiryo",
                "font_size": 30,
                "text_color": "#CBD2DC",
                "stroke_color": "#7F8998",
                "background_color": "#687282",
                "confidence": 0.94,
                "bounding_box": box(122, 278, 522, 322),
            },
        ],
        "background": {"base": "#5C6674", "panel": "#788293", "panel_outline": "#929CAE"},
    },
    {
        "sample_id": "multi_position_telop",
        "file_name": "sample_multi_position_telop.mp4",
        "duration_ms": 5000,
        "recommended_frame_interval_seconds": 1.0,
        "description": "上部、中央、下部に複数位置のテロップを含む。",
        "segments": [
            {
                "segment_id": "seg-0001",
                "start_timestamp_ms": 1000,
                "end_timestamp_ms": 3000,
                "text": "上段テロップ",
                "text_type": "title",
                "font_family": "Meiryo",
                "font_size": 34,
                "text_color": "#FFF2A8",
                "stroke_color": "#202020",
                "background_color": None,
                "confidence": 0.97,
                "bounding_box": box(198, 42, 446, 90),
            },
            {
                "segment_id": "seg-0002",
                "start_timestamp_ms": 1000,
                "end_timestamp_ms": 3000,
                "text": "下段テロップ",
                "text_type": "caption_band",
                "font_family": "Meiryo",
                "font_size": 32,
                "text_color": "#FFFFFF",
                "stroke_color": "#000000",
                "background_color": "#244E96",
                "confidence": 0.97,
                "bounding_box": box(170, 276, 472, 322),
            },
            {
                "segment_id": "seg-0003",
                "start_timestamp_ms": 3000,
                "end_timestamp_ms": 5000,
                "text": "中央テロップ",
                "text_type": "headline",
                "font_family": "Meiryo",
                "font_size": 36,
                "text_color": "#FFEE7C",
                "stroke_color": "#242424",
                "background_color": None,
                "confidence": 0.96,
                "bounding_box": box(182, 152, 470, 202),
            },
        ],
        "background": {"base": "#1B1E25", "panel": "#2B313E", "panel_outline": "#495165"},
    },
    {
        "sample_id": "short_duration_telop",
        "file_name": "sample_short_duration_telop.mp4",
        "duration_ms": 4500,
        "recommended_frame_interval_seconds": 0.25,
        "description": "1 秒未満の短時間表示テロップを複数含む。",
        "segments": [
            {
                "segment_id": "seg-0001",
                "start_timestamp_ms": 500,
                "end_timestamp_ms": 1250,
                "text": "短い表示その1",
                "text_type": "caption_band",
                "font_family": "Meiryo",
                "font_size": 30,
                "text_color": "#FFFFFF",
                "stroke_color": "#000000",
                "background_color": "#3B4C7F",
                "confidence": 0.96,
                "bounding_box": box(146, 278, 494, 322),
            },
            {
                "segment_id": "seg-0002",
                "start_timestamp_ms": 1750,
                "end_timestamp_ms": 2250,
                "text": "短い表示その2",
                "text_type": "caption_band",
                "font_family": "Meiryo",
                "font_size": 30,
                "text_color": "#FFFFFF",
                "stroke_color": "#000000",
                "background_color": "#2E6C68",
                "confidence": 0.95,
                "bounding_box": box(146, 278, 494, 322),
            },
            {
                "segment_id": "seg-0003",
                "start_timestamp_ms": 3000,
                "end_timestamp_ms": 3500,
                "text": "短い表示その3",
                "text_type": "caption_band",
                "font_family": "Meiryo",
                "font_size": 30,
                "text_color": "#FFFFFF",
                "stroke_color": "#000000",
                "background_color": "#7A4830",
                "confidence": 0.95,
                "bounding_box": box(146, 278, 494, 322),
            },
        ],
        "background": {"base": "#18212B", "panel": "#263748", "panel_outline": "#41586F"},
    },
]


def main() -> None:
    ROOT.mkdir(parents=True, exist_ok=True)

    catalog = []
    for sample in SAMPLES:
        video_path = ROOT / sample["file_name"]
        ground_truth_path = ROOT / f"{sample['sample_id']}_ground_truth.json"
        write_video(sample, video_path)
        write_ground_truth(sample, video_path, ground_truth_path)
        catalog.append(
            {
                "sample_id": sample["sample_id"],
                "video_path": video_path.name,
                "ground_truth_path": ground_truth_path.name,
                "description": sample["description"],
                "recommended_frame_interval_seconds": sample["recommended_frame_interval_seconds"],
            }
        )

    (ROOT / "samples.json").write_text(
        json.dumps(catalog, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def write_video(sample: dict, video_path: Path) -> None:
    fourcc = cv2.VideoWriter_fourcc(*"mp4v")
    writer = cv2.VideoWriter(str(video_path), fourcc, FPS, (WIDTH, HEIGHT))
    if not writer.isOpened():
        raise RuntimeError(f"failed to open video writer: {video_path}")

    frame_count = int(round(sample["duration_ms"] / 1000 * FPS))
    for frame_number in range(frame_count):
        timestamp_ms = int(frame_number / FPS * 1000)
        writer.write(render_frame(sample, timestamp_ms))

    writer.release()


def render_frame(sample: dict, timestamp_ms: int) -> np.ndarray:
    background = sample["background"]
    image = Image.new("RGB", (WIDTH, HEIGHT), background["base"])
    draw = ImageDraw.Draw(image)

    draw.rectangle((28, 28, WIDTH - 28, HEIGHT - 28), fill=background["panel"], outline=background["panel_outline"], width=2)

    for segment in active_segments(sample["segments"], timestamp_ms):
        draw_segment(draw, segment)

    return cv2.cvtColor(np.array(image), cv2.COLOR_RGB2BGR)


def active_segments(segments: list[dict], timestamp_ms: int) -> list[dict]:
    return [
        segment
        for segment in segments
        if segment["start_timestamp_ms"] <= timestamp_ms < segment["end_timestamp_ms"]
    ]


def draw_segment(draw: ImageDraw.ImageDraw, segment: dict) -> None:
    font = ImageFont.truetype(str(FONT_PATH), segment["font_size"])
    points = segment["bounding_box"]
    left = min(point["x"] for point in points)
    top = min(point["y"] for point in points)
    right = max(point["x"] for point in points)
    bottom = max(point["y"] for point in points)

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


def write_ground_truth(sample: dict, video_path: Path, ground_truth_path: Path) -> None:
    payload = {
        "schema_version": "1.0.0",
        "video": {
            "file_name": video_path.name,
            "duration_ms": sample["duration_ms"],
            "width": WIDTH,
            "height": HEIGHT,
            "fps": FPS,
        },
        "evaluation": {
            "sample_id": sample["sample_id"],
            "description": sample["description"],
            "recommended_frame_interval_seconds": sample["recommended_frame_interval_seconds"],
        },
        "segments": [
            {
                key: value
                for key, value in segment.items()
                if key != "bounding_box"
            }
            for segment in sample["segments"]
        ],
    }
    ground_truth_path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
if __name__ == "__main__":
    main()
