#!/usr/bin/env python3
"""Convert ALLCH translation JSON files to the game's three-column CSV format."""

from __future__ import annotations

import argparse
import csv
import json
import os
import re
import tempfile
from pathlib import Path
from typing import Any


DEFAULT_INPUT_DIR = Path("resources/20260801/24023703allch json")
DEFAULT_OUTPUT_DIR = Path("resources/20260801/24023703allch")
CSV_COLUMNS = ("key", "original", "translation")


class ConversionError(Exception):
    """Raised when an input JSON file cannot be converted safely."""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Convert all ALLCH JSON translation files to CSV files."
    )
    parser.add_argument(
        "--input-dir",
        type=Path,
        default=DEFAULT_INPUT_DIR,
        help=f"Directory containing JSON files (default: {DEFAULT_INPUT_DIR})",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=DEFAULT_OUTPUT_DIR,
        help=f"Directory for generated CSV files (default: {DEFAULT_OUTPUT_DIR})",
    )
    return parser.parse_args()


def as_csv_value(value: Any) -> str:
    """Convert JSON scalar values to the string representation expected by CSV."""
    return "" if value is None else str(value)


def output_name(input_path: Path) -> str:
    """Map e.g. ``speech-ALLCH-24023703.csv.json`` to ``speech-24023703.csv``."""
    if input_path.suffix.lower() != ".json":
        raise ConversionError(f"Input file does not have a .json suffix: {input_path}")

    name = input_path.name[: -len(input_path.suffix)]
    name = re.sub(r"-ALLCH(?=-|\.)", "", name, flags=re.IGNORECASE)
    if not name.lower().endswith(".csv"):
        name += ".csv"
    return name


def read_rows(input_path: Path) -> list[list[str]]:
    try:
        with input_path.open("r", encoding="utf-8-sig") as file:
            payload = json.load(file)
    except (OSError, json.JSONDecodeError) as error:
        raise ConversionError(f"Could not read JSON file {input_path}: {error}") from error

    if not isinstance(payload, list):
        raise ConversionError(f"Expected a JSON array in {input_path}")

    rows: list[list[str]] = []
    for index, item in enumerate(payload, start=1):
        if not isinstance(item, dict):
            raise ConversionError(
                f"Expected an object at row {index} in {input_path}, got {type(item).__name__}"
            )

        missing = [column for column in CSV_COLUMNS if column not in item]
        if missing:
            missing_columns = ", ".join(missing)
            raise ConversionError(
                f"Missing field(s) at row {index} in {input_path}: {missing_columns}"
            )

        rows.append([as_csv_value(item[column]) for column in CSV_COLUMNS])

    return rows


def write_csv(output_path: Path, rows: list[list[str]]) -> None:
    """Write atomically so an interrupted conversion does not leave a partial CSV."""
    temporary_path: str | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8-sig",
            newline="",
            dir=output_path.parent,
            prefix=f".{output_path.name}.",
            suffix=".tmp",
            delete=False,
        ) as file:
            temporary_path = file.name
            writer = csv.writer(file, lineterminator="\n")
            writer.writerows(rows)

        os.replace(temporary_path, output_path)
        temporary_path = None
    finally:
        if temporary_path is not None:
            try:
                os.unlink(temporary_path)
            except FileNotFoundError:
                pass


def convert(input_dir: Path, output_dir: Path) -> int:
    if not input_dir.is_dir():
        raise ConversionError(f"Input directory does not exist: {input_dir}")

    input_files = sorted(input_dir.glob("*.json"))
    if not input_files:
        raise ConversionError(f"No .json files found in {input_dir}")

    output_paths: dict[Path, Path] = {}
    for input_path in input_files:
        target = output_dir / output_name(input_path)
        previous = output_paths.get(target)
        if previous is not None:
            raise ConversionError(
                f"Multiple JSON files map to the same output {target}: {previous} and {input_path}"
            )
        output_paths[target] = input_path

    output_dir.mkdir(parents=True, exist_ok=True)
    for output_path, input_path in output_paths.items():
        rows = read_rows(input_path)
        write_csv(output_path, rows)
        print(f"Converted {input_path.name} -> {output_path.name} ({len(rows)} rows)")

    return len(output_paths)


def main() -> int:
    args = parse_args()
    try:
        count = convert(args.input_dir, args.output_dir)
    except ConversionError as error:
        print(f"Error: {error}")
        return 1

    print(f"Converted {count} file(s) to {args.output_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
