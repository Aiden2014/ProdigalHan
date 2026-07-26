#!/usr/bin/env python3
"""Migrate translations into CSV files extracted from Steam build 24023703."""

from __future__ import annotations

import argparse
import csv
import os
import shutil
import sys
import tempfile
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
    new_paths = [
        new_path
        for new_path in sorted(resources_dir.glob(f"*{suffix}"))
        if not new_path.name.removesuffix(suffix).endswith("-ALLCH")
    ]
    if not new_paths:
        raise MigrationError(f"No *{suffix} files found in {resources_dir}")

    pairs = []
    for new_path in new_paths:
        stem = new_path.name.removesuffix(suffix)
        old_path = resources_dir / f"{stem}.csv"
        if not old_path.is_file():
            raise MigrationError(
                f"Missing previous-version CSV for {new_path.name}: {old_path}"
            )
        pairs.append((old_path, new_path))
    return pairs


def require_two_columns(path: Path, rows: list[list[str]]) -> None:
    for line_number, row in enumerate(rows, 1):
        if len(row) < 2:
            raise MigrationError(f"{path}:{line_number} has fewer than two columns")


def build_file_plan(old_path: Path, new_path: Path) -> FilePlan:
    old_rows = read_csv(old_path)
    new_rows = read_csv(new_path)
    require_two_columns(old_path, old_rows)
    require_two_columns(new_path, new_rows)

    translations: dict[str, str] = {}
    for line_number, row in enumerate(old_rows, 1):
        key = row[0].casefold()
        if key in translations:
            raise MigrationError(
                f"{old_path}:{line_number} duplicates first-column key "
                f"{row[0]!r} after case-folding"
            )
        translations[key] = row[2] if len(row) >= 3 else ""

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


def migration_outputs(
    resources_dir: Path, plans: list[FilePlan]
) -> list[tuple[Path, list[list[str]]]]:
    outputs = []
    for plan in plans:
        outputs.extend(
            (
                (plan.new_path, plan.migrated_rows),
                (
                    resources_dir / "old" / plan.old_path.name,
                    plan.unmatched_old_rows,
                ),
                (
                    resources_dir / "new" / plan.new_path.name,
                    plan.unmatched_new_rows,
                ),
            )
        )
    return outputs


def cleanup_transaction_files(paths: list[Path]) -> list[str]:
    errors = []
    for path in paths:
        try:
            path.unlink(missing_ok=True)
        except OSError as error:
            errors.append(f"{path}: {error}")
    return errors


def stage_outputs(
    outputs: list[tuple[Path, list[list[str]]]],
) -> dict[Path, Path]:
    staged: dict[Path, Path] = {}
    current_target: Path | None = None
    try:
        for current_target, rows in outputs:
            current_target.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.NamedTemporaryFile(
                "w",
                encoding="utf-8",
                newline="",
                dir=current_target.parent,
                prefix=f".{current_target.name}.",
                suffix=".tmp",
                delete=False,
            ) as handle:
                staged[current_target] = Path(handle.name)
                handle.write("\ufeff")
                csv.writer(handle).writerows(rows)
    except OSError as error:
        cleanup_errors = cleanup_transaction_files(list(staged.values()))
        detail = f"Cannot stage migration output {current_target}: {error}"
        if cleanup_errors:
            detail += f"; cleanup failed: {'; '.join(cleanup_errors)}"
        raise MigrationError(detail) from error
    return staged


def backup_existing_outputs(outputs: list[tuple[Path, list[list[str]]]]) -> dict[Path, Path]:
    backups: dict[Path, Path] = {}
    current_target: Path | None = None
    try:
        for current_target, _ in outputs:
            if not current_target.exists():
                continue
            with tempfile.NamedTemporaryFile(
                "wb",
                dir=current_target.parent,
                prefix=f".{current_target.name}.",
                suffix=".bak",
                delete=False,
            ) as handle:
                backup_path = Path(handle.name)
            backups[current_target] = backup_path
            shutil.copyfile(current_target, backup_path)
    except OSError as error:
        cleanup_errors = cleanup_transaction_files(list(backups.values()))
        detail = f"Cannot back up migration output {current_target}: {error}"
        if cleanup_errors:
            detail += f"; cleanup failed: {'; '.join(cleanup_errors)}"
        raise MigrationError(detail) from error
    return backups


def rollback_outputs(
    outputs: list[tuple[Path, list[list[str]]]], backups: dict[Path, Path]
) -> tuple[list[str], set[Path]]:
    errors = []
    retained_backups = set()
    for target, _ in outputs:
        try:
            if target in backups:
                os.replace(backups[target], target)
            else:
                target.unlink(missing_ok=True)
        except OSError as error:
            if target in backups:
                backup = backups[target]
                retained_backups.add(backup)
                errors.append(
                    f"{target}: {error}; recovery backup retained at "
                    f"{backup.resolve()}"
                )
            else:
                errors.append(f"{target}: {error}")
    return errors, retained_backups


def replace_outputs_transactionally(
    outputs: list[tuple[Path, list[list[str]]]],
) -> None:
    staged = stage_outputs(outputs)
    try:
        backups = backup_existing_outputs(outputs)
    except MigrationError as error:
        cleanup_errors = cleanup_transaction_files(list(staged.values()))
        if cleanup_errors:
            raise MigrationError(
                f"{error}; staged cleanup failed: {'; '.join(cleanup_errors)}"
            ) from error
        raise

    current_target: Path | None = None
    try:
        for current_target, _ in outputs:
            os.replace(staged[current_target], current_target)
    except OSError as error:
        rollback_errors, retained_backups = rollback_outputs(outputs, backups)
        cleanup_errors = cleanup_transaction_files(
            [
                *staged.values(),
                *(
                    backup
                    for backup in backups.values()
                    if backup not in retained_backups
                ),
            ]
        )
        detail = f"Cannot replace migration output {current_target}: {error}"
        if rollback_errors:
            detail += f"; rollback failed: {'; '.join(rollback_errors)}"
        if cleanup_errors:
            detail += f"; cleanup failed: {'; '.join(cleanup_errors)}"
        raise MigrationError(detail) from error

    cleanup_errors = cleanup_transaction_files(list(backups.values()))
    if cleanup_errors:
        raise MigrationError(
            f"Cannot clean up migration backups: {'; '.join(cleanup_errors)}"
        )


def migrate_plans(resources_dir: Path, plans: list[FilePlan]) -> MigrationSummary:
    replace_outputs_transactionally(migration_outputs(resources_dir, plans))
    return MigrationSummary(
        files=len(plans),
        matched=sum(plan.matched for plan in plans),
        old_only=sum(len(plan.unmatched_old_rows) for plan in plans),
        new_only=sum(len(plan.unmatched_new_rows) for plan in plans),
    )


def migrate(resources_dir: Path) -> MigrationSummary:
    return migrate_plans(resources_dir, plan_migration(resources_dir))


def default_resources_dir() -> Path:
    return Path(__file__).resolve().parents[1] / "resources"


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Migrate previous translations into Steam build 24023703 CSV files."
    )
    parser.add_argument(
        "--resources-dir",
        type=Path,
        default=default_resources_dir(),
        help="directory containing old and *-24023703.csv files",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        plans = plan_migration(args.resources_dir)
        for plan in plans:
            print(
                f"{plan.new_path.name}: matched={plan.matched} "
                f"old_only={len(plan.unmatched_old_rows)} "
                f"new_only={len(plan.unmatched_new_rows)}"
            )
        summary = migrate_plans(args.resources_dir, plans)
    except MigrationError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    print(
        f"files={summary.files} matched={summary.matched} "
        f"old_only={summary.old_only} new_only={summary.new_only}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
