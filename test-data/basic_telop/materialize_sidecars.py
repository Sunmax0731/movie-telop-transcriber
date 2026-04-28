from __future__ import annotations

import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent
OCR_BY_TIMESTAMP_PATH = ROOT / "expected_ocr_by_timestamp.json"
TIMESTAMP_PATTERN = re.compile(r"_(\d+)ms\.png$", re.IGNORECASE)


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: python materialize_sidecars.py <frames_directory>", file=sys.stderr)
        return 2

    frames_directory = Path(sys.argv[1])
    if not frames_directory.is_dir():
        print(f"frames directory not found: {frames_directory}", file=sys.stderr)
        return 2

    expected = json.loads(OCR_BY_TIMESTAMP_PATH.read_text(encoding="utf-8"))
    written = 0

    for frame_path in sorted(frames_directory.glob("*.png")):
        match = TIMESTAMP_PATTERN.search(frame_path.name)
        if not match:
            continue

        timestamp_ms = int(match.group(1))
        detections = expected.get(str(timestamp_ms), [])
        response = {
            "request_id": f"sidecar-{frame_path.stem}",
            "status": "success",
            "frame_index": parse_frame_index(frame_path.name),
            "timestamp_ms": timestamp_ms,
            "detections": detections,
            "error": None,
        }

        sidecar_path = frame_path.with_suffix(".ocr.json")
        sidecar_path.write_text(json.dumps(response, ensure_ascii=False, indent=2), encoding="utf-8")
        written += 1

    print(f"wrote {written} OCR sidecar file(s) to {frames_directory}")
    return 0


def parse_frame_index(file_name: str) -> int:
    parts = file_name.split("_")
    if len(parts) >= 2 and parts[1].isdigit():
        return int(parts[1])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
