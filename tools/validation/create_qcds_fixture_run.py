#!/usr/bin/env python3
"""Create a fixture segments.json and summary.json from ground truth."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--ground-truth", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--sample-id", required=True)
    parser.add_argument("--frame-interval-seconds", type=float, required=True)
    args = parser.parse_args()

    ground_truth = json.loads(args.ground_truth.read_text(encoding="utf-8"))
    segments = ground_truth.get("segments", [])
    video = ground_truth.get("video", {})
    output_root = args.output_root
    output_dir = output_root / "output"
    logs_dir = output_root / "logs"
    output_dir.mkdir(parents=True, exist_ok=True)
    logs_dir.mkdir(parents=True, exist_ok=True)

    segment_payload = {
        "schema_version": "1.0.0",
        "source_video": {
            "file_path": str((args.ground_truth.parent / video.get("file_name", "")).resolve()),
            "file_name": video.get("file_name"),
            "duration_ms": video.get("duration_ms"),
            "width": video.get("width"),
            "height": video.get("height"),
            "fps": video.get("fps"),
            "codec": "fixture",
        },
        "processing_settings": {
            "frame_interval_seconds": args.frame_interval_seconds,
            "ocr_engine": "fixture-ground-truth",
            "offline_mode": True,
        },
        "frames": [],
        "segments": [
            {
                "segment_id": segment["segment_id"],
                "start_timestamp_ms": segment["start_timestamp_ms"],
                "end_timestamp_ms": segment["end_timestamp_ms"],
                "text": segment["text"],
                "text_type": segment.get("text_type"),
                "font_family": segment.get("font_family"),
                "font_size": segment.get("font_size"),
                "font_size_unit": "px",
                "text_color": segment.get("text_color"),
                "stroke_color": segment.get("stroke_color"),
                "background_color": segment.get("background_color"),
                "confidence": segment.get("confidence"),
                "source_frame_count": max(
                    1,
                    int(round((segment["end_timestamp_ms"] - segment["start_timestamp_ms"]) / (args.frame_interval_seconds * 1000)))
                ),
            }
            for segment in segments
        ],
        "run_metadata": {
            "application_version": "fixture",
            "warning_count": 0,
            "error_count": 0,
        },
    }

    summary_payload = {
        "run_id": f"fixture_{args.sample_id}",
        "status": "success",
        "source_video_path": segment_payload["source_video"]["file_path"],
        "frame_interval_seconds": args.frame_interval_seconds,
        "ocr_engine": "fixture-ground-truth",
        "frame_count": len(segment_payload["segments"]),
        "detection_count": len(segment_payload["segments"]),
        "segment_count": len(segment_payload["segments"]),
        "warning_count": 0,
        "error_count": 0,
        "processing_time_ms": 1,
        "work_directory": str(output_root.resolve()),
        "output_directory": str(output_dir.resolve()),
        "json_path": str((output_dir / "segments.json").resolve()),
    }

    (output_dir / "segments.json").write_text(
        json.dumps(segment_payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    (logs_dir / "summary.json").write_text(
        json.dumps(summary_payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
