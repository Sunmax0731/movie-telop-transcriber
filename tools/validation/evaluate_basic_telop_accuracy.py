#!/usr/bin/env python3
"""Evaluate OCR and attribute-analysis accuracy for the basic telop sample.

The current app can run OCR through an external JSON worker or through frame
sidecar files. This script evaluates the deterministic sidecar sample so the
test result can be reproduced without a bundled OCR engine.
"""

from __future__ import annotations

import argparse
import json
from dataclasses import dataclass
from pathlib import Path
from statistics import mean
from typing import Any


@dataclass(frozen=True)
class EvaluatedSegment:
    segment_id: str
    start_timestamp_ms: int
    end_timestamp_ms: int
    text: str
    text_type: str
    font_family: str | None
    font_size: float | None
    text_color: str | None
    stroke_color: str | None
    background_color: str | None
    confidence: float | None
    source_frame_count: int


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--sample-dir",
        type=Path,
        default=Path("test-data/basic_telop"),
        help="Directory containing ground_truth.json and expected_ocr_by_timestamp.json.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        help="Optional markdown report path.",
    )
    args = parser.parse_args()

    ground_truth = load_json(args.sample_dir / "ground_truth.json")
    ocr_by_timestamp = load_json(args.sample_dir / "expected_ocr_by_timestamp.json")
    expected_segments = ground_truth["segments"]
    evaluated_segments = evaluate_segments(ocr_by_timestamp)
    report = build_report(args.sample_dir, expected_segments, evaluated_segments, ocr_by_timestamp)

    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(report, encoding="utf-8", newline="\n")
    else:
        print(report)

    return 0


def load_json(path: Path) -> Any:
    with path.open(encoding="utf-8") as file:
        return json.load(file)


def evaluate_segments(ocr_by_timestamp: dict[str, list[dict[str, Any]]]) -> list[EvaluatedSegment]:
    timestamps = sorted(int(value) for value in ocr_by_timestamp)
    frame_interval_ms = infer_frame_interval_ms(timestamps)
    max_gap_ms = max(frame_interval_ms, round(frame_interval_ms * 1.5))
    completed: list[SegmentBuilder] = []
    active_by_text: dict[str, SegmentBuilder] = {}

    for timestamp_ms in timestamps:
        detections = ocr_by_timestamp[str(timestamp_ms)]
        active_texts = set()

        for detection in detections:
            text = detection["text"]
            active_texts.add(text)
            font_size = estimate_font_size(detection.get("bounding_box", []))
            confidence = detection.get("confidence")
            active = active_by_text.get(text)

            if active and timestamp_ms - active.last_timestamp_ms <= max_gap_ms:
                active.extend(timestamp_ms, frame_interval_ms, font_size, confidence)
                continue

            if active:
                completed.append(active)

            active_by_text[text] = SegmentBuilder(
                start_timestamp_ms=timestamp_ms,
                default_duration_ms=frame_interval_ms,
                text=text,
                font_size=font_size,
                confidence=confidence,
            )

        for text in list(active_by_text):
            if text not in active_texts and timestamp_ms - active_by_text[text].last_timestamp_ms > max_gap_ms:
                completed.append(active_by_text.pop(text))

    completed.extend(active_by_text.values())

    return [
        builder.to_segment(f"seg-{index + 1:04d}")
        for index, builder in enumerate(sorted(completed, key=lambda value: value.start_timestamp_ms))
    ]


def infer_frame_interval_ms(timestamps: list[int]) -> int:
    diffs = [
        right - left
        for left, right in zip(timestamps, timestamps[1:])
        if right > left
    ]
    return min(diffs) if diffs else 1000


def estimate_font_size(bounding_box: list[dict[str, Any]]) -> float | None:
    if not bounding_box:
        return None

    y_values = [float(point["y"]) for point in bounding_box]
    height = abs(max(y_values) - min(y_values))
    return round(height, 1) if height > 0 else None


@dataclass
class SegmentBuilder:
    start_timestamp_ms: int
    default_duration_ms: int
    text: str
    font_size: float | None
    confidence: float | None

    def __post_init__(self) -> None:
        self.end_timestamp_ms = self.start_timestamp_ms + self.default_duration_ms
        self.last_timestamp_ms = self.start_timestamp_ms
        self.source_frame_count = 1
        self.font_sizes: list[float] = []
        self.confidences: list[float] = []
        if self.font_size is not None:
            self.font_sizes.append(self.font_size)
        if self.confidence is not None:
            self.confidences.append(float(self.confidence))

    def extend(
        self,
        timestamp_ms: int,
        default_duration_ms: int,
        font_size: float | None,
        confidence: float | None,
    ) -> None:
        self.last_timestamp_ms = timestamp_ms
        self.end_timestamp_ms = timestamp_ms + default_duration_ms
        self.source_frame_count += 1
        if font_size is not None:
            self.font_sizes.append(font_size)
        if confidence is not None:
            self.confidences.append(float(confidence))

    def to_segment(self, segment_id: str) -> EvaluatedSegment:
        return EvaluatedSegment(
            segment_id=segment_id,
            start_timestamp_ms=self.start_timestamp_ms,
            end_timestamp_ms=self.end_timestamp_ms,
            text=self.text,
            text_type="unknown",
            font_family=None,
            font_size=round(mean(self.font_sizes), 1) if self.font_sizes else None,
            text_color=None,
            stroke_color=None,
            background_color=None,
            confidence=round(mean(self.confidences), 4) if self.confidences else None,
            source_frame_count=self.source_frame_count,
        )


def build_report(
    sample_dir: Path,
    expected_segments: list[dict[str, Any]],
    evaluated_segments: list[EvaluatedSegment],
    ocr_by_timestamp: dict[str, list[dict[str, Any]]],
) -> str:
    exact_text_matches = sum(
        1
        for expected, actual in zip(expected_segments, evaluated_segments)
        if expected["text"] == actual.text
    )
    exact_time_matches = sum(
        1
        for expected, actual in zip(expected_segments, evaluated_segments)
        if expected["start_timestamp_ms"] == actual.start_timestamp_ms
        and expected["end_timestamp_ms"] == actual.end_timestamp_ms
    )
    non_empty_frames = sum(1 for detections in ocr_by_timestamp.values() if detections)

    lines = [
        "# OCRと属性解析の精度検証結果",
        "",
        "## 1. 文書情報",
        "- 対象 Issue: `#45`",
        "- 実施日: 2026-04-28",
        "- 対象データ: `test-data/basic_telop/sample_basic_telop.mp4`",
        "- 検証方法: `expected_ocr_by_timestamp.json` を OCR worker / sidecar 相当の入力として扱い、`ground_truth.json` と比較",
        "",
        "## 2. 実行コマンド",
        "```powershell",
        "python tools/validation/evaluate_basic_telop_accuracy.py --output docs/test-results/2026-04-28_ocr属性解析精度検証.md",
        "```",
        "",
        "## 3. サマリ",
        f"- OCR テキスト完全一致: {exact_text_matches} / {len(expected_segments)} セグメント",
        f"- 開始/終了時刻完全一致: {exact_time_matches} / {len(expected_segments)} セグメント",
        f"- OCR 検出ありフレーム: {non_empty_frames} / {len(ocr_by_timestamp)} フレーム",
        "- 属性解析の現行実装は OCR 矩形から `font_size` を推定し、`font_family`、色、背景色、厳密な種別は未推定値として扱う。",
        "- 外部 OCR worker または動画ごとの `.ocr.json` sidecar がない任意動画では、現行仕様上は空検出になる。",
        "",
        "## 4. セグメント別結果",
        "| segment | 期待テキスト | 検証テキスト | テキスト | 期待時刻 | 検証時刻 | 時刻 | font_size | 未推定属性 |",
        "| --- | --- | --- | --- | --- | --- | --- | --- | --- |",
    ]

    for index, expected in enumerate(expected_segments):
        actual = evaluated_segments[index] if index < len(evaluated_segments) else None
        if actual is None:
            lines.append(
                f"| {expected['segment_id']} | {expected['text']} | - | NG | "
                f"{expected['start_timestamp_ms']}-{expected['end_timestamp_ms']} | - | NG | - | 全属性 |"
            )
            continue

        text_status = "OK" if expected["text"] == actual.text else "NG"
        time_status = (
            "OK"
            if expected["start_timestamp_ms"] == actual.start_timestamp_ms
            and expected["end_timestamp_ms"] == actual.end_timestamp_ms
            else "NG"
        )
        missing_attributes = [
            name
            for name, expected_value, actual_value in [
                ("text_type", expected.get("text_type"), actual.text_type),
                ("font_family", expected.get("font_family"), actual.font_family),
                ("text_color", expected.get("text_color"), actual.text_color),
                ("stroke_color", expected.get("stroke_color"), actual.stroke_color),
                ("background_color", expected.get("background_color"), actual.background_color),
            ]
            if expected_value != actual_value
        ]
        lines.append(
            f"| {expected['segment_id']} | {expected['text']} | {actual.text} | {text_status} | "
            f"{expected['start_timestamp_ms']}-{expected['end_timestamp_ms']} | "
            f"{actual.start_timestamp_ms}-{actual.end_timestamp_ms} | {time_status} | "
            f"期待 {expected.get('font_size')} / 推定 {actual.font_size} | "
            f"{', '.join(missing_attributes) if missing_attributes else '-'} |"
        )

    lines.extend(
        [
            "",
            "## 5. 判断",
            "- サンプル sidecar を使う範囲では、文字列とセグメント時刻は期待値と一致する。",
            "- 現行の属性解析は OCR 矩形からの高さ推定に限られるため、フォント名、文字色、枠色、背景色、装飾種別の実用精度評価は未成立。",
            "- `認識されず` の報告は、実 OCR worker または対象動画専用 sidecar がない状態では空検出になるため、現行仕様と整合する。",
            "- 実動画の OCR 精度評価には、PaddleOCR / Tesseract / Windows OCR などの worker 実体、または対象動画ごとの正解 sidecar が必要。",
            "",
            "## 6. 既知制約",
            "- `materialize_sidecars.py` は `sample_basic_telop.mp4` 専用であり、任意動画には使わない。",
            "- `font_size` は表示フォントサイズではなく OCR 矩形の高さ相当として推定される。",
            "- `font_family`、`text_color`、`stroke_color`、`background_color`、精密な `text_type` は現行の内部属性解析だけでは算出しない。",
            "- 実 OCR worker の採用、同梱、導入手順はリリース工程の配布構成確認と合わせて再評価する。",
            "",
            "## 7. 入力ファイル",
            f"- `{sample_dir / 'ground_truth.json'}`",
            f"- `{sample_dir / 'expected_ocr_by_timestamp.json'}`",
        ]
    )

    return "\n".join(lines) + "\n"


if __name__ == "__main__":
    raise SystemExit(main())
