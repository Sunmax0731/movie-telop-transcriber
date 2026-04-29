from __future__ import annotations

from pathlib import Path

import cv2


ROOT = Path(__file__).resolve().parent
SOURCE_PATH = ROOT.parent / "basic_telop" / "sample_basic_telop.mp4"
TARGETS = [
    ("sample_basic_telop_60s.mp4", 12),
    ("sample_basic_telop_180s.mp4", 36),
]


def main() -> None:
    if not SOURCE_PATH.exists():
        raise FileNotFoundError(f"Source sample was not found: {SOURCE_PATH}")

    for target_name, repeat_count in TARGETS:
        write_repeated_video(SOURCE_PATH, ROOT / target_name, repeat_count)


def write_repeated_video(source_path: Path, target_path: Path, repeat_count: int) -> None:
    capture = cv2.VideoCapture(str(source_path))
    if not capture.isOpened():
        raise RuntimeError(f"Failed to open source video: {source_path}")

    fps = capture.get(cv2.CAP_PROP_FPS) or 10.0
    width = int(capture.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(capture.get(cv2.CAP_PROP_FRAME_HEIGHT))
    fourcc = cv2.VideoWriter_fourcc(*"mp4v")

    frames: list[object] = []
    while True:
        success, frame = capture.read()
        if not success:
            break
        frames.append(frame)

    capture.release()

    if not frames:
        raise RuntimeError(f"No frames were read from source video: {source_path}")

    writer = cv2.VideoWriter(str(target_path), fourcc, fps, (width, height))
    if not writer.isOpened():
        raise RuntimeError(f"Failed to create target video: {target_path}")

    try:
        for _ in range(repeat_count):
            for frame in frames:
                writer.write(frame)
    finally:
        writer.release()


if __name__ == "__main__":
    main()
