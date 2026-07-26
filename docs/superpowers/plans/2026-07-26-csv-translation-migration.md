# CSV Translation Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a dependency-free Python script that transfers previous-version translations into Steam build `24023703` CSV files by matching the first column without regard to case, while recording unmatched rows in both directions.

**Architecture:** Keep the implementation in one focused script with pure planning functions separated from filesystem writes. Discover and validate every pair, create immutable per-file merge plans in memory, and only then write migrated CSVs and unmatched reports; a small CLI calls that workflow with the repository's `resources/` directory by default.

**Tech Stack:** Python 3 standard library (`argparse`, `csv`, `dataclasses`, `pathlib`, `tempfile`, `unittest`).

## Global Constraints

- Scan `resources/*-24023703.csv`; pair each file with the same name after removing `-24023703`.
- Do not treat `-ALLCH.csv` files as migration inputs.
- Normalize first-column keys with `str.casefold()` only; keep whitespace, punctuation, asterisks, and all other characters significant.
- Require at least two columns per row; a missing old third column means an empty translation.
- Reject duplicate normalized keys in an old file; allow them in a new file.
- Validate every pair before creating directories or changing files.
- Overwrite the new file's third column and never append a fourth column on repeated runs.
- Preserve original row shapes in unmatched reports.
- Write migrated files and reports as UTF-8 with BOM using standard CSV quoting.
- Write one old and one new unmatched report for every pair, including empty reports.
- Do not delete unrelated files from `resources/old` or `resources/new`.
- Add no third-party dependencies and do not modify the C# plugin or existing scripts.

## File Structure

- Create `scripts/merge_translation_csv.py`: discovery, parsing, validation, in-memory merge planning, UTF-8 BOM CSV writing, summaries, and CLI entry point.
- Create `tests/test_merge_translation_csv.py`: temporary-directory behavioral and error-path tests against the real script functions.

---

### Task 1: Case-insensitive merge and unmatched reports

**Files:**
- Create: `scripts/merge_translation_csv.py`
- Create: `tests/test_merge_translation_csv.py`

**Interfaces:**
- Consumes: A `pathlib.Path` containing old `*.csv` and new `*-24023703.csv` pairs.
- Produces: `MigrationError`, `FilePlan`, `MigrationSummary`, `plan_migration(resources_dir: Path) -> list[FilePlan]`, and `migrate(resources_dir: Path) -> MigrationSummary`.

- [ ] **Step 1: Write all failing Task 1 behavioral tests**

Create `tests/test_merge_translation_csv.py` with helpers that use real CSV files and a test proving first-column case-folding, replacement of an existing third column, punctuation preservation, and Chinese translation preservation:

```python
import csv
import tempfile
import unittest
from pathlib import Path

from scripts.merge_translation_csv import MigrationError, migrate


def write_csv(path: Path, rows: list[list[str]], encoding: str = "utf-8") -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding=encoding, newline="") as handle:
        csv.writer(handle).writerows(rows)


def read_csv(path: Path) -> list[list[str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.reader(handle))


class MigrationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.resources = Path(self.temporary_directory.name) / "resources"
        self.resources.mkdir()

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_matches_first_column_with_casefold_and_replaces_translation(self) -> None:
        write_csv(
            self.resources / "speech.csv",
            [["SCENE-HELLO, \"TRAVELER\"!", "HELLO, \"TRAVELER\"!", "你好，旅人！"]],
            encoding="utf-8-sig",
        )
        write_csv(
            self.resources / "speech-24023703.csv",
            [["Scene-Hello, \"Traveler\"!", "Hello, \"Traveler\"!", "stale", "discard"]],
        )

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [["Scene-Hello, \"Traveler\"!", "Hello, \"Traveler\"!", "你好，旅人！"]],
        )
        self.assertEqual(summary.matched, 1)
        self.assertEqual(summary.old_only, 0)
        self.assertEqual(summary.new_only, 0)
        self.assertTrue(
            (self.resources / "speech-24023703.csv").read_bytes().startswith(b"\xef\xbb\xbf")
        )

    def test_repeated_new_keys_receive_the_same_translation(self) -> None:
        write_csv(self.resources / "speaker.csv", [["SISKA", "SISKA", "西斯卡"]])
        write_csv(
            self.resources / "speaker-24023703.csv",
            [["Siska", "Siska"], ["SISKA", "SISKA"]],
        )

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speaker-24023703.csv"),
            [["Siska", "Siska", "西斯卡"], ["SISKA", "SISKA", "西斯卡"]],
        )
        self.assertEqual(summary.matched, 2)

    def test_writes_original_old_only_and_new_only_rows(self) -> None:
        old_only = ["OLD-SCENE", "Removed text", "旧剧情"]
        new_only = ["NEW-SCENE", "Added text"]
        write_csv(self.resources / "speech.csv", [old_only])
        write_csv(self.resources / "speech-24023703.csv", [new_only])

        summary = migrate(self.resources)

        self.assertEqual(read_csv(self.resources / "old" / "speech.csv"), [old_only])
        self.assertEqual(
            read_csv(self.resources / "new" / "speech-24023703.csv"), [new_only]
        )
        self.assertTrue(
            (self.resources / "old" / "speech.csv").read_bytes().startswith(b"\xef\xbb\xbf")
        )
        self.assertTrue(
            (self.resources / "new" / "speech-24023703.csv").read_bytes().startswith(
                b"\xef\xbb\xbf"
            )
        )
        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [["NEW-SCENE", "Added text", ""]],
        )
        self.assertEqual((summary.old_only, summary.new_only), (1, 1))

    def test_missing_old_translation_is_empty_and_second_run_is_idempotent(self) -> None:
        write_csv(self.resources / "item.csv", [["ITEM-0", ""]])
        write_csv(self.resources / "item-24023703.csv", [["item-0", ""]])

        migrate(self.resources)
        first_run = (self.resources / "item-24023703.csv").read_bytes()
        migrate(self.resources)

        self.assertEqual((self.resources / "item-24023703.csv").read_bytes(), first_run)
        self.assertEqual(read_csv(self.resources / "item-24023703.csv"), [["item-0", "", ""]])
        self.assertEqual(read_csv(self.resources / "old" / "item.csv"), [])
        self.assertEqual(read_csv(self.resources / "new" / "item-24023703.csv"), [])

    def test_rejects_csv_parse_and_decode_errors(self) -> None:
        for directory_name, invalid_bytes in (
            ("parse-error", b'"unterminated'),
            ("decode-error", b"\xff"),
        ):
            with self.subTest(directory_name=directory_name):
                resources = self.resources / directory_name
                resources.mkdir()
                (resources / "speech.csv").write_bytes(invalid_bytes)
                write_csv(resources / "speech-24023703.csv", [["KEY", "Text"]])

                with self.assertRaisesRegex(MigrationError, "Cannot read CSV"):
                    migrate(resources)
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
py -m unittest tests.test_merge_translation_csv.MigrationTests -v
```

Expected: all five tests error because `scripts.merge_translation_csv` does not exist.

- [ ] **Step 3: Implement the minimal in-memory plan and write path**

Create `scripts/merge_translation_csv.py` with these exact public types and functions:

```python
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
```

- [ ] **Step 4: Run Task 1 tests and verify GREEN**

Run the Step 2 command again.

Expected: five tests pass with no warnings or tracebacks, and the generated migrated files and reports start with UTF-8 BOMs.

- [ ] **Step 5: Commit Task 1**

```powershell
git add -- scripts/merge_translation_csv.py tests/test_merge_translation_csv.py
git commit -m "feat(csv): migrate translations by context key"
```

### Task 2: Validation, CLI reporting, and repository preflight

**Files:**
- Modify: `scripts/merge_translation_csv.py`
- Modify: `tests/test_merge_translation_csv.py`

**Interfaces:**
- Consumes: Task 1's `plan_migration(resources_dir: Path)` and `migrate(resources_dir: Path)` functions.
- Produces: `main(argv: list[str] | None = None) -> int`; clear `MigrationError` failures; per-file and total console counts.

- [ ] **Step 1: Write failing validation tests that prove no output occurs**

Update the import to include `MigrationError` and add:

```python
from scripts.merge_translation_csv import MigrationError, migrate


class ValidationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.resources = Path(self.temporary_directory.name) / "resources"
        self.resources.mkdir()

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_rejects_duplicate_old_keys_before_writing(self) -> None:
        write_csv(
            self.resources / "speech.csv",
            [["KEY", "One", "一"], ["key", "Two", "二"]],
        )
        new_path = self.resources / "speech-24023703.csv"
        write_csv(new_path, [["Key", "One"]])
        original_new_bytes = new_path.read_bytes()

        with self.assertRaisesRegex(MigrationError, "duplicates first-column key"):
            migrate(self.resources)

        self.assertEqual(new_path.read_bytes(), original_new_bytes)
        self.assertFalse((self.resources / "old").exists())
        self.assertFalse((self.resources / "new").exists())

    def test_rejects_any_invalid_pair_before_writing_valid_pairs(self) -> None:
        valid_new = self.resources / "a-24023703.csv"
        write_csv(self.resources / "a.csv", [["A", "A", "甲"]])
        write_csv(valid_new, [["a", "A"]])
        original_valid_bytes = valid_new.read_bytes()
        write_csv(self.resources / "b.csv", [["BROKEN"]])
        write_csv(self.resources / "b-24023703.csv", [["B", "B"]])

        with self.assertRaisesRegex(MigrationError, "fewer than two columns"):
            migrate(self.resources)

        self.assertEqual(valid_new.read_bytes(), original_valid_bytes)
        self.assertFalse((self.resources / "old").exists())
        self.assertFalse((self.resources / "new").exists())

    def test_rejects_missing_pair_and_empty_input_directory(self) -> None:
        with self.assertRaisesRegex(MigrationError, "No .* files found"):
            migrate(self.resources)

        write_csv(self.resources / "speech-24023703.csv", [["KEY", "Text"]])
        with self.assertRaisesRegex(MigrationError, "Missing previous-version CSV"):
            migrate(self.resources)
```

- [ ] **Step 2: Run validation tests and verify RED**

Run:

```powershell
py -m unittest tests.test_merge_translation_csv.ValidationTests -v
```

Expected: at least one failure if Task 1 does not yet enforce all validation-before-write cases.

- [ ] **Step 3: Tighten discovery and validation without adding writes**

Replace `discover_pairs` and add `require_two_columns`:

```python
def discover_pairs(resources_dir: Path) -> list[tuple[Path, Path]]:
    suffix = f"-{BUILD_ID}.csv"
    new_paths = sorted(resources_dir.glob(f"*{suffix}"))
    if not new_paths:
        raise MigrationError(f"No *{suffix} files found in {resources_dir}")

    pairs = []
    for new_path in new_paths:
        old_path = resources_dir / f"{new_path.name.removesuffix(suffix)}.csv"
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
```

Replace the first half of `build_file_plan` so malformed rows and duplicate old keys are rejected explicitly:

```python
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

    unmatched_old_rows = [
        row.copy() for row in old_rows if row[0].casefold() not in new_keys
    ]
    return FilePlan(
        old_path=old_path,
        new_path=new_path,
        migrated_rows=migrated_rows,
        unmatched_old_rows=unmatched_old_rows,
        unmatched_new_rows=unmatched_new_rows,
        matched=matched,
    )
```

`read_csv` remains strict and wraps operating-system, decoding, and CSV parser errors in `MigrationError`. `migrate` must retain this ordering:

```python
def migrate(resources_dir: Path) -> MigrationSummary:
    plans = plan_migration(resources_dir)
    # No directory creation or write_csv call may occur above this line.
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
```

- [ ] **Step 4: Run validation tests and verify GREEN**

Run the Step 2 command again.

Expected: three validation tests pass, and the tests confirm neither report directory was created on validation failure.

- [ ] **Step 5: Write failing CLI tests for success and errors**

Add imports and tests:

```python
import contextlib
import io

from scripts.merge_translation_csv import MigrationError, main, migrate


class CliTests(unittest.TestCase):
    def test_main_reports_totals(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            resources = Path(directory) / "resources"
            resources.mkdir()
            write_csv(resources / "speech.csv", [["KEY", "Text", "译文"]])
            write_csv(resources / "speech-24023703.csv", [["key", "Text"]])
            stdout = io.StringIO()

            with contextlib.redirect_stdout(stdout):
                exit_code = main(["--resources-dir", str(resources)])

            self.assertEqual(exit_code, 0)
            self.assertIn("files=1 matched=1 old_only=0 new_only=0", stdout.getvalue())

    def test_main_returns_nonzero_and_prints_validation_error(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            resources = Path(directory) / "resources"
            resources.mkdir()
            stderr = io.StringIO()

            with contextlib.redirect_stderr(stderr):
                exit_code = main(["--resources-dir", str(resources)])

            self.assertEqual(exit_code, 1)
            self.assertIn("No *-24023703.csv files found", stderr.getvalue())
```

- [ ] **Step 6: Run CLI tests and verify RED**

Run:

```powershell
py -m unittest tests.test_merge_translation_csv.CliTests -v
```

Expected: `ImportError` because `main` does not exist.

- [ ] **Step 7: Implement the CLI and per-file reporting**

Add to `scripts/merge_translation_csv.py`:

```python
import argparse
import sys


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
```

Refactor Task 1's write loop into the interface used above, without changing `migrate`:

```python
def migrate_plans(resources_dir: Path, plans: list[FilePlan]) -> MigrationSummary:
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


def migrate(resources_dir: Path) -> MigrationSummary:
    return migrate_plans(resources_dir, plan_migration(resources_dir))
```

- [ ] **Step 8: Run CLI and all unit tests and verify GREEN**

Run:

```powershell
py -m unittest tests.test_merge_translation_csv -v
```

Expected: all ten tests pass with no warnings or tracebacks.

- [ ] **Step 9: Compile the script and perform a read-only preflight on real resources**

Run:

```powershell
py -m py_compile scripts/merge_translation_csv.py tests/test_merge_translation_csv.py
py -c "from pathlib import Path; from scripts.merge_translation_csv import plan_migration; plans = plan_migration(Path('resources')); print(f'validated_files={len(plans)} matched={sum(p.matched for p in plans)} old_only={sum(len(p.unmatched_old_rows) for p in plans)} new_only={sum(len(p.unmatched_new_rows) for p in plans)}')"
```

Expected: compilation exits successfully; preflight reports `validated_files=14` and does not create `resources/old`, `resources/new`, or change any `*-24023703.csv` file.

- [ ] **Step 10: Review the final diff and commit Task 2**

```powershell
git diff --check
git status --short
git add -- scripts/merge_translation_csv.py tests/test_merge_translation_csv.py
git commit -m "feat(csv): validate migration inputs and report results"
```

Only the two task files may be staged; leave `Plugin.cs`, `StringDumper.cs`, `.claude/`, and existing untracked scripts untouched.

## Final Verification

- [ ] Run `py -m unittest tests.test_merge_translation_csv -v` and confirm all ten tests pass.
- [ ] Run `py -m py_compile scripts/merge_translation_csv.py tests/test_merge_translation_csv.py` and confirm exit code 0.
- [ ] Run the read-only `plan_migration(Path('resources'))` preflight and confirm all 14 pairs validate.
- [ ] Run `git diff --check HEAD~2..HEAD` and confirm no whitespace errors in the implementation commits.
- [ ] Review `git status --short` and confirm all pre-existing user changes remain untouched.
