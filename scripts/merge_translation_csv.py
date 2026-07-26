#!/usr/bin/env python3
"""Migrate translations into CSV files extracted from Steam build 24023703."""

from __future__ import annotations

import csv
from dataclasses import dataclass
from pathlib import Path

BUILD_ID = "24023703"


class MigrationError(Exception):
    """Raised when migration input is unsafe or invalid."""


@dataclass(frozen=True)
class FilePlan:
    old_path: Path
    new_path: Path
    migrated_rows: list[list[str]]
    unmatched_old_rows: list[list[str]]
    unmatched_new_rows: list[list[str]]
    matched: int


@dataclass(frozen=True)
class MigrationSummary:
    files: int
    matched: int
    old_only: int
    new_only: int


def read_csv(path: Path) -> list[list[str]]:
    try:
        with path.open("r", encoding="utf-8-sig", newline="") as handle:
            return list(csv.reader(handle, strict=True))
    except (OSError, UnicodeError, csv.Error) as error:
        raise MigrationError(f"Cannot read CSV {path}: {error}") from error


def discover_pairs(resources_dir: Path) -> list[tuple[Path, Path]]:
    suffix = f"-{BUILD_ID}.csv"
    new_paths = sorted(resources_dir.glob(f"*{suffix}"))
    return [
        (resources_dir / f"{new_path.name.removesuffix(suffix)}.csv", new_path)
        for new_path in new_paths
    ]


def build_file_plan(old_path: Path, new_path: Path) -> FilePlan:
    old_rows = read_csv(old_path)
    new_rows = read_csv(new_path)

    translations = {
        row[0].casefold(): row[2] if len(row) >= 3 else ""
        for row in old_rows
    }

    new_keys = {row[0].casefold() for row in new_rows}
    migrated_rows = []
    unmatched_new_rows = []
    matched = 0
    for row in new_rows:
        key = row[0].casefold()
        if key in translations:
            matched += 1
            translation = translations[key]
        else:
            translation = ""
            unmatched_new_rows.append(row.copy())
        migrated_rows.append([row[0], row[1], translation])

    unmatched_old_rows = [row.copy() for row in old_rows if row[0].casefold() not in new_keys]
    return FilePlan(
        old_path=old_path,
        new_path=new_path,
        migrated_rows=migrated_rows,
        unmatched_old_rows=unmatched_old_rows,
        unmatched_new_rows=unmatched_new_rows,
        matched=matched,
    )


def plan_migration(resources_dir: Path) -> list[FilePlan]:
    return [build_file_plan(old_path, new_path) for old_path, new_path in discover_pairs(resources_dir)]


def write_csv(path: Path, rows: list[list[str]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        csv.writer(handle).writerows(rows)


def migrate(resources_dir: Path) -> MigrationSummary:
    plans = plan_migration(resources_dir)
    for plan in plans:
        write_csv(plan.new_path, plan.migrated_rows)
        write_csv(resources_dir / "old" / plan.old_path.name, plan.unmatched_old_rows)
        write_csv(resources_dir / "new" / plan.new_path.name, plan.unmatched_new_rows)
    return MigrationSummary(
        files=len(plans),
        matched=sum(plan.matched for plan in plans),
        old_only=sum(len(plan.unmatched_old_rows) for plan in plans),
        new_only=sum(len(plan.unmatched_new_rows) for plan in plans),
    )
