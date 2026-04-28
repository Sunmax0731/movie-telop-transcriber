#!/usr/bin/env python3
"""Build a QCDS OCR evaluation report from app output and ground truth."""

from __future__ import annotations

import argparse
import json
import re
from datetime import datetime
from pathlib import Path
from statistics import mean
from typing import Any


MATCH_THRESHOLD = 0.35


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--ground-truth",
        type=Path,
        default=Path("test-data/basic_telop/ground_truth.json"),
        help="Ground-truth JSON path.",
    )
    parser.add_argument(
        "--segments",
        type=Path,
        required=True,
        help="App output segments.json path.",
    )
    parser.add_argument(
        "--summary",
        type=Path,
        help="Optional logs/summary.json path for processing-time and count metrics.",
    )
    parser.add_argument(
        "--sample-id",
        default="basic_telop",
        help="Representative sample ID used in the report.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        help="Optional Markdown report output path.",
    )
    parser.add_argument(
        "--metrics-output",
        type=Path,
        help="Optional machine-readable metrics JSON output path.",
    )
    parser.add_argument(
        "--previous-metrics",
        type=Path,
        help="Optional previous metrics JSON path for before/after comparison.",
    )
    args = parser.parse_args()

    ground_truth = load_json(args.ground_truth)
    actual_output = load_json(args.segments)
    summary = load_json(args.summary) if args.summary else {}
    previous_metrics = load_json(args.previous_metrics) if args.previous_metrics else None

    evaluation = evaluate(
        expected_segments=ground_truth.get("segments", []),
        actual_segments=actual_output.get("segments", []),
        actual_output=actual_output,
        summary=summary,
    )
    metrics = evaluation["metrics"]
    metrics["sample_id"] = args.sample_id
    metrics["ground_truth_path"] = str(args.ground_truth)
    metrics["segments_path"] = str(args.segments)
    if args.summary:
        metrics["summary_path"] = str(args.summary)

    report = build_report(
        sample_id=args.sample_id,
        ground_truth_path=args.ground_truth,
        segments_path=args.segments,
        summary_path=args.summary,
        metrics=metrics,
        matches=evaluation["matches"],
        missing=evaluation["missing"],
        extras=evaluation["extras"],
        previous_metrics=previous_metrics,
    )

    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(report, encoding="utf-8", newline="\n")
    else:
        print(report)

    if args.metrics_output:
        args.metrics_output.parent.mkdir(parents=True, exist_ok=True)
        args.metrics_output.write_text(
            json.dumps(metrics, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )

    return 0


def load_json(path: Path | None) -> Any:
    if path is None:
        return {}

    with path.open(encoding="utf-8") as file:
        return json.load(file)


def evaluate(
    expected_segments: list[dict[str, Any]],
    actual_segments: list[dict[str, Any]],
    actual_output: dict[str, Any],
    summary: dict[str, Any],
) -> dict[str, Any]:
    matches, missing, used_actual_indexes = match_segments(expected_segments, actual_segments)
    extras = [
        segment
        for index, segment in enumerate(actual_segments)
        if index not in used_actual_indexes
    ]

    text_exact_count = sum(1 for match in matches if match["text_exact"])
    text_similarities = [match["text_similarity"] for match in matches]
    start_errors = [match["start_error_ms"] for match in matches]
    end_errors = [match["end_error_ms"] for match in matches]
    overall_character_accuracy = (
        sum(text_similarities) / len(expected_segments)
        if expected_segments
        else 1.0
    )

    run_metadata = actual_output.get("run_metadata", {})
    processing_time_ms = infer_processing_time_ms(summary, run_metadata)
    warning_count = first_number(summary.get("warning_count"), run_metadata.get("warning_count"), 0)
    error_count = first_number(summary.get("error_count"), run_metadata.get("error_count"), 0)
    frame_count = first_number(summary.get("frame_count"), len(actual_output.get("frames", [])), 0)
    detection_count = first_number(
        summary.get("detection_count"),
        sum(len(frame.get("detections", [])) for frame in actual_output.get("frames", [])),
        0,
    )

    metrics = {
        "expected_segment_count": len(expected_segments),
        "actual_segment_count": len(actual_segments),
        "matched_segment_count": len(matches),
        "missing_segment_count": len(missing),
        "extra_segment_count": len(extras),
        "text_exact_count": text_exact_count,
        "text_exact_rate": ratio(text_exact_count, len(expected_segments)),
        "character_accuracy": overall_character_accuracy,
        "matched_character_accuracy": mean(text_similarities) if text_similarities else 0.0,
        "avg_start_error_ms": mean(start_errors) if start_errors else None,
        "max_start_error_ms": max(start_errors) if start_errors else None,
        "avg_end_error_ms": mean(end_errors) if end_errors else None,
        "max_end_error_ms": max(end_errors) if end_errors else None,
        "processing_time_ms": processing_time_ms,
        "processing_time_seconds": round(processing_time_ms / 1000, 3) if processing_time_ms is not None else None,
        "frame_count": int(frame_count),
        "detection_count": int(detection_count),
        "warning_count": int(warning_count),
        "error_count": int(error_count),
        "ocr_engine": summary.get("ocr_engine") or actual_output.get("processing_settings", {}).get("ocr_engine"),
        "frame_interval_seconds": summary.get("frame_interval_seconds")
        or actual_output.get("processing_settings", {}).get("frame_interval_seconds"),
        "run_id": summary.get("run_id"),
        "status": summary.get("status"),
    }

    return {
        "metrics": metrics,
        "matches": matches,
        "missing": missing,
        "extras": extras,
    }


def match_segments(
    expected_segments: list[dict[str, Any]],
    actual_segments: list[dict[str, Any]],
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], set[int]]:
    matches: list[dict[str, Any]] = []
    missing: list[dict[str, Any]] = []
    used_actual_indexes: set[int] = set()

    for expected in expected_segments:
        best_index: int | None = None
        best_score = 0.0
        best_text_similarity = 0.0
        best_time_score = 0.0

        for index, actual in enumerate(actual_segments):
            if index in used_actual_indexes:
                continue

            text_score = text_similarity(expected.get("text", ""), actual.get("text", ""))
            time_score = segment_time_score(expected, actual)
            score = (text_score * 0.75) + (time_score * 0.25)
            if score > best_score:
                best_index = index
                best_score = score
                best_text_similarity = text_score
                best_time_score = time_score

        if best_index is None or best_score < MATCH_THRESHOLD:
            missing.append(expected)
            continue

        actual = actual_segments[best_index]
        used_actual_indexes.add(best_index)
        start_error_ms = abs(int(actual.get("start_timestamp_ms", 0)) - int(expected.get("start_timestamp_ms", 0)))
        end_error_ms = abs(int(actual.get("end_timestamp_ms", 0)) - int(expected.get("end_timestamp_ms", 0)))
        matches.append(
            {
                "expected": expected,
                "actual": actual,
                "score": best_score,
                "text_similarity": best_text_similarity,
                "time_score": best_time_score,
                "text_exact": normalize_text(expected.get("text", "")) == normalize_text(actual.get("text", "")),
                "start_error_ms": start_error_ms,
                "end_error_ms": end_error_ms,
            }
        )

    return matches, missing, used_actual_indexes


def normalize_text(value: Any) -> str:
    return "".join(str(value or "").split())


def text_similarity(expected: Any, actual: Any) -> float:
    left = normalize_text(expected)
    right = normalize_text(actual)
    if not left and not right:
        return 1.0
    if not left or not right:
        return 0.0

    distance = levenshtein_distance(left, right)
    return 1.0 - (distance / max(len(left), len(right)))


def levenshtein_distance(left: str, right: str) -> int:
    if left == right:
        return 0
    if len(left) < len(right):
        left, right = right, left
    if not right:
        return len(left)

    previous = list(range(len(right) + 1))
    for left_index, left_char in enumerate(left, start=1):
        current = [left_index]
        for right_index, right_char in enumerate(right, start=1):
            insert_cost = current[right_index - 1] + 1
            delete_cost = previous[right_index] + 1
            replace_cost = previous[right_index - 1] + (left_char != right_char)
            current.append(min(insert_cost, delete_cost, replace_cost))
        previous = current

    return previous[-1]


def segment_time_score(expected: dict[str, Any], actual: dict[str, Any]) -> float:
    expected_duration = max(
        1,
        int(expected.get("end_timestamp_ms", 0)) - int(expected.get("start_timestamp_ms", 0)),
    )
    tolerance_ms = max(1000, expected_duration)
    diff_ms = abs(int(actual.get("start_timestamp_ms", 0)) - int(expected.get("start_timestamp_ms", 0)))
    diff_ms += abs(int(actual.get("end_timestamp_ms", 0)) - int(expected.get("end_timestamp_ms", 0)))
    return max(0.0, 1.0 - min(1.0, diff_ms / tolerance_ms))


def infer_processing_time_ms(summary: dict[str, Any], run_metadata: dict[str, Any]) -> int | None:
    metadata_value = run_metadata.get("processing_time_ms")
    if metadata_value is not None:
        return int(float(metadata_value))

    started_at = parse_datetime(summary.get("started_at"))
    completed_at = parse_datetime(summary.get("completed_at"))
    if started_at and completed_at:
        return int((completed_at - started_at).total_seconds() * 1000)

    return None


def parse_datetime(value: Any) -> datetime | None:
    if not value:
        return None

    text = str(value)
    match = re.match(r"^(.*T\d{2}:\d{2}:\d{2})\.(\d{1,})([+-]\d{2}:\d{2}|Z)$", text)
    if match:
        text = f"{match.group(1)}.{match.group(2)[:6]}{match.group(3)}"
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"

    try:
        return datetime.fromisoformat(text)
    except ValueError:
        return None


def first_number(*values: Any) -> float:
    for value in values:
        if value is None:
            continue
        try:
            return float(value)
        except (TypeError, ValueError):
            continue
    return 0.0


def ratio(count: int, total: int) -> float:
    return count / total if total else 1.0


def build_report(
    sample_id: str,
    ground_truth_path: Path,
    segments_path: Path,
    summary_path: Path | None,
    metrics: dict[str, Any],
    matches: list[dict[str, Any]],
    missing: list[dict[str, Any]],
    extras: list[dict[str, Any]],
    previous_metrics: dict[str, Any] | None,
) -> str:
    metric_rows = [
        ("認識文字列完全一致率", "text_exact_rate", "{:.1%}"),
        ("文字単位一致率", "character_accuracy", "{:.1%}"),
        ("欠落セグメント数", "missing_segment_count", "{:.0f}"),
        ("余計な検出セグメント数", "extra_segment_count", "{:.0f}"),
        ("平均開始時刻誤差", "avg_start_error_ms", "{:.0f} ms"),
        ("平均終了時刻誤差", "avg_end_error_ms", "{:.0f} ms"),
        ("処理時間", "processing_time_seconds", "{:.3f} sec"),
        ("エラー件数", "error_count", "{:.0f}"),
    ]

    lines = [
        f"# QCDS評価レポート: {sample_id}",
        "",
        "## 1. 文書情報",
        "- 対象 Issue: `#90`",
        "- 実施日: 2026-04-29",
        f"- 代表動画 ID: `{sample_id}`",
        f"- OCR エンジン: `{metrics.get('ocr_engine') or '-'}`",
        f"- Run ID: `{metrics.get('run_id') or '-'}`",
        "",
        "## 2. 入力",
        f"- 正解データ: `{ground_truth_path}`",
        f"- 解析結果: `{segments_path}`",
        f"- サマリ: `{summary_path if summary_path else '-'}`",
        "",
        "## 3. 再生成コマンド",
        "```powershell",
        "python tools/validation/evaluate_qcds_report.py `",
        f"  --ground-truth {ground_truth_path} `",
        f"  --segments {segments_path} `",
        f"  --summary {summary_path if summary_path else ''} `",
        f"  --sample-id {sample_id} `",
        "  --output docs/test-results/2026-04-29_qcds_basic_telop_report.md `",
        "  --metrics-output docs/test-results/2026-04-29_qcds_basic_telop_metrics.json",
        "```",
        "",
        "## 4. QCDSサマリ",
        "| 観点 | 指標 | 今回値 | 前回値 | 差分 |",
        "| --- | --- | ---: | ---: | ---: |",
    ]

    for label, key, fmt in metric_rows:
        current = metrics.get(key)
        previous = previous_metrics.get(key) if previous_metrics else None
        lines.append(
            f"| {qcds_axis_for_metric(key)} | {label} | {format_metric(current, fmt)} | "
            f"{format_metric(previous, fmt)} | {format_delta(current, previous, fmt)} |"
        )

    lines.extend(
        [
            "",
            "## 5. 件数",
            f"- 期待セグメント: {metrics['expected_segment_count']}",
            f"- 実セグメント: {metrics['actual_segment_count']}",
            f"- マッチしたセグメント: {metrics['matched_segment_count']}",
            f"- 欠落セグメント: {metrics['missing_segment_count']}",
            f"- 余計な検出セグメント: {metrics['extra_segment_count']}",
            f"- フレーム数: {metrics['frame_count']}",
            f"- OCR 検出数: {metrics['detection_count']}",
            f"- 警告 / エラー: {metrics['warning_count']} / {metrics['error_count']}",
            "",
            "## 6. セグメント別比較",
            "| 期待ID | 実ID | 期待テキスト | 実テキスト | 文字一致 | 開始誤差 | 終了誤差 | confidence |",
            "| --- | --- | --- | --- | ---: | ---: | ---: | ---: |",
        ]
    )

    for match in matches:
        expected = match["expected"]
        actual = match["actual"]
        lines.append(
            f"| {expected.get('segment_id', '-')} | {actual.get('segment_id', '-')} | "
            f"{escape_table(expected.get('text', ''))} | {escape_table(actual.get('text', ''))} | "
            f"{match['text_similarity']:.1%} | {match['start_error_ms']} ms | "
            f"{match['end_error_ms']} ms | {format_confidence(actual.get('confidence'))} |"
        )

    lines.extend(["", "## 7. 欠落セグメント", "| 期待ID | 期待テキスト | 期待時刻 |", "| --- | --- | --- |"])
    if missing:
        for segment in missing:
            lines.append(
                f"| {segment.get('segment_id', '-')} | {escape_table(segment.get('text', ''))} | "
                f"{segment.get('start_timestamp_ms', '-')}-{segment.get('end_timestamp_ms', '-')} ms |"
            )
    else:
        lines.append("| - | - | - |")

    lines.extend(["", "## 8. 余計な検出セグメント", "| 実ID | テキスト | 時刻 | confidence |", "| --- | --- | --- | ---: |"])
    if extras:
        for segment in extras:
            lines.append(
                f"| {segment.get('segment_id', '-')} | {escape_table(segment.get('text', ''))} | "
                f"{segment.get('start_timestamp_ms', '-')}-{segment.get('end_timestamp_ms', '-')} ms | "
                f"{format_confidence(segment.get('confidence'))} |"
            )
    else:
        lines.append("| - | - | - | - |")

    lines.extend(
        [
            "",
            "## 9. 自動評価と手動確認の分離",
            "- 自動評価: 文字列一致、文字単位一致、欠落、余計な検出、開始/終了時刻誤差、処理時間、警告/エラー件数。",
            "- 手動確認: プレビュー上の矩形位置、テロップ種別ラベルの妥当性、実利用上の読みやすさ、GUI 操作感。",
            "",
            "## 10. 判定メモ",
            "- `basic_telop` では正解 2 セグメントの文字列と時刻は一致した。",
            "- 一方で、動画内の時刻表示が余計な検出セグメントとして残っているため、リリース前の継続評価では extra segment count を重点指標にする。",
            "- このレポート形式は `--previous-metrics` に前回 JSON を指定すると、改善前後の差分欄を埋められる。",
            "",
        ]
    )

    return "\n".join(lines)


def qcds_axis_for_metric(key: str) -> str:
    if key in {"text_exact_rate", "character_accuracy", "missing_segment_count", "extra_segment_count"}:
        return "Q"
    if key == "processing_time_seconds":
        return "C"
    if key in {"avg_start_error_ms", "avg_end_error_ms"}:
        return "D"
    return "S"


def format_metric(value: Any, fmt: str) -> str:
    if value is None:
        return "-"
    try:
        return fmt.format(float(value))
    except (TypeError, ValueError):
        return str(value)


def format_delta(current: Any, previous: Any, fmt: str) -> str:
    if current is None or previous is None:
        return "-"
    try:
        return fmt.format(float(current) - float(previous))
    except (TypeError, ValueError):
        return "-"


def format_confidence(value: Any) -> str:
    if value is None:
        return "-"
    try:
        return f"{float(value):.1%}"
    except (TypeError, ValueError):
        return str(value)


def escape_table(value: Any) -> str:
    return str(value or "-").replace("|", "\\|").replace("\n", "<br>")


if __name__ == "__main__":
    raise SystemExit(main())
