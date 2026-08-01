#!/usr/bin/env python3
"""Fill translations for speecher-24023703.csv from speecher.csv."""

from __future__ import annotations

import csv
from pathlib import Path


SOURCE_24023703 = Path("resources/24023703/resources/speecher-24023703.csv")
SOURCE_ALLCH = Path("resources/24023703/resources_allch/speecher.csv")
OUTPUT_PATH = Path("resources/24023703/resources_allch/speecher-24023703.csv")


def read_csv(path: Path) -> list[list[str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.reader(handle))


def normalize_key(value: str) -> str:
    return value.lstrip("\ufeff").casefold()


def build_translation_map(rows: list[list[str]]) -> dict[str, str]:
    mapping: dict[str, str] = {}
    for row in rows:
        if len(row) < 3:
            continue
        key = normalize_key(row[1])
        if key not in mapping:
            mapping[key] = row[2]
    return mapping


def main() -> int:
    rows_24023703 = read_csv(SOURCE_24023703)
    rows_allch = read_csv(SOURCE_ALLCH)
    translation_map = build_translation_map(rows_allch)

    output_rows: list[list[str]] = []
    unmatched_rows: list[list[str]] = []

    for row in rows_24023703:
        if len(row) < 2:
            raise ValueError(
                f"{SOURCE_24023703} has a row with fewer than two columns: {row!r}"
            )
        key = normalize_key(row[1])
        translation = translation_map.get(key, "")
        output_rows.append([row[0], row[1], translation])
        if translation == "":
            unmatched_rows.append([row[0], row[1]])

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with OUTPUT_PATH.open("w", encoding="utf-8-sig", newline="") as handle:
        csv.writer(handle).writerows(output_rows)

    print(f"saved: {OUTPUT_PATH}")
    if unmatched_rows:
        print("unmatched rows:")
        for row in unmatched_rows:
            print(f"{row[0]},{row[1]}")
    else:
        print("unmatched rows: none")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
