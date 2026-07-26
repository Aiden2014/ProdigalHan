# CSV ALLCH Merge Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a boolean/CLI-selected ALLCH migration mode that pairs `-ALLCH.csv` with `-ALLCH-24023703.csv`, writes mode-specific unmatched reports, and leaves normal mode unchanged.

**Architecture:** Thread an `allch: bool = False` option through discovery, planning, output generation, migration, and CLI parsing. The option selects exact filename suffixes and report directories; all CSV matching stages and transactional output remain shared.

**Tech Stack:** Python 3 standard library (`argparse`, `csv`, `dataclasses`, `pathlib`, `unittest`); existing CSV transaction and rollback implementation.

## Global Constraints

- Normal mode keeps the current file selection: old `<name>.csv`, new `<name>-24023703.csv`, reports `old` and `new`.
- ALLCH mode selects old `<name>-ALLCH.csv`, new `<name>-ALLCH-24023703.csv`, reports `old-allch` and `new-allch`.
- ALLCH output is written to the selected new path and retains the `-ALLCH-24023703.csv` filename.
- Normal mode continues excluding ALLCH files from discovery; ALLCH mode never mixes normal and ALLCH pairs.
- Match only complete first-column keys and preserve original row text.
- Preserve existing exact, structural, generated-index, and fuzzy matching stages and thresholds.
- Preserve validation, UTF-8 BOM output, staging, backups, rollback, recovery-backup retention, and unmatched report locations selected by mode.
- Run every Python command with `py` (use `py -B` for read-only verification where bytecode suppression matters).

---

### Task 1: Add failing normal/ALLCH mode tests

**Files:**
- Modify: `tests/test_merge_translation_csv.py`

**Interfaces:**
- Add the optional `allch: bool = False` argument to the tested migration APIs.
- Continue consuming `MigrationSummary` counters and `matched` totals.

- [ ] **Step 1: Add an ALLCH pairing and output test**

Create old `achievement-ALLCH.csv` and new `achievement-ALLCH-24023703.csv`
fixtures in the temporary resources directory. Call `migrate(resources, allch=True)`
and assert the new file is translated in place, the output filename remains
`achievement-ALLCH-24023703.csv`, and the summary reports one match.

- [ ] **Step 2: Add mode-specific unmatched report tests**

Use an unmatched old and new row in ALLCH fixtures. Assert reports are written
to `resources/old-allch/achievement-ALLCH.csv` and
`resources/new-allch/achievement-ALLCH-24023703.csv`, and that normal `old` and
`new` report directories are not used for those rows.

- [ ] **Step 3: Add normal-mode compatibility and validation tests**

Keep an existing normal pair and call `migrate(resources)` without the flag;
assert its behavior and report paths remain unchanged. Add a validation case
where `allch=True` has only a normal pair, expecting `MigrationError` for no
ALLCH inputs or a missing ALLCH pair.

- [ ] **Step 4: Add CLI `--allch` coverage**

Invoke `main(["--resources-dir", str(resources), "--allch"])` against an ALLCH
fixture and assert the per-file and total lines include the ALLCH filename and
all counters in the existing order.

- [ ] **Step 5: Run the focused suite and confirm RED**

```powershell
py -m unittest tests.test_merge_translation_csv -v
```

Expected: new tests fail because the APIs do not yet accept `allch` and the
CLI has no `--allch` option; existing tests continue to exercise normal mode.

- [ ] **Step 6: Commit the RED tests**

```powershell
git add tests/test_merge_translation_csv.py
git commit -m "test(csv): specify ALLCH merge mode"
```

---

### Task 2: Implement mode-aware discovery, outputs, and CLI

**Files:**
- Modify: `scripts/merge_translation_csv.py`

**Interfaces:**
- `discover_pairs(resources_dir: Path, allch: bool = False) -> list[tuple[Path, Path]]`
- `migration_outputs(resources_dir: Path, plans: list[FilePlan], allch: bool = False) -> list[tuple[Path, list[list[str]]]]`
- `plan_migration(resources_dir: Path, allch: bool = False) -> list[FilePlan]`
- `migrate_plans(resources_dir: Path, plans: list[FilePlan], allch: bool = False) -> MigrationSummary`
- `migrate(resources_dir: Path, allch: bool = False) -> MigrationSummary`
- `parse_args` accepts `--allch` and returns `args.allch: bool`.

- [ ] **Step 1: Add mode suffix and report-directory selection**

In `discover_pairs`, derive exact suffixes:

```python
if allch:
    new_suffix = f"-ALLCH-{BUILD_ID}.csv"
    old_suffix = "-ALLCH.csv"
else:
    new_suffix = f"-{BUILD_ID}.csv"
    old_suffix = ".csv"
```

Discover only `*{new_suffix}` files. In normal mode continue excluding stems
ending in `-ALLCH`; in ALLCH mode derive the old path by removing
`new_suffix` and appending `old_suffix`.

- [ ] **Step 2: Thread the mode through planning and output generation**

Pass `allch` through `plan_migration`, `migrate`, and `migrate_plans`. In
`migration_outputs`, select report directories:

```python
report_old_dir = resources_dir / ("old-allch" if allch else "old")
report_new_dir = resources_dir / ("new-allch" if allch else "new")
```

Keep the migrated output target as `plan.new_path`, preserving the selected
normal or ALLCH filename.

- [ ] **Step 3: Add the CLI boolean flag**

Add:

```python
parser.add_argument(
    "--allch",
    action="store_true",
    help="merge -ALLCH.csv into -ALLCH-24023703.csv files",
)
```

Pass `args.allch` to both `plan_migration` and `migrate_plans`.

- [ ] **Step 4: Preserve normal-mode API compatibility**

Keep all new parameters defaulted to `False`, so existing callers and tests
continue using normal mode without changes. Do not alter matching logic or
counter semantics.

- [ ] **Step 5: Run focused tests and compilation GREEN**

```powershell
py -m unittest tests.test_merge_translation_csv -v
py -m py_compile scripts\merge_translation_csv.py tests\test_merge_translation_csv.py
```

Expected: all focused tests pass.

- [ ] **Step 6: Commit the implementation**

```powershell
git add scripts/merge_translation_csv.py tests/test_merge_translation_csv.py
git commit -m "feat(csv): add ALLCH merge mode"
```

---

### Task 3: Verify normal and ALLCH resources safely

**Files:**
- Read: `resources/**/*.csv`
- Verify: `scripts/merge_translation_csv.py`
- Verify: `tests/test_merge_translation_csv.py`

- [ ] **Step 1: Run normal-mode read-only preflight**

Call `plan_migration(resources_dir, allch=False)` only, hash all files before
and after, and print the existing counters. Do not migrate real resources.

- [ ] **Step 2: Run ALLCH read-only preflight**

Call `plan_migration(resources_dir, allch=True)` only. If no real ALLCH new
files exist, verify the expected `MigrationError` and do not create fixtures in
the real resources directory. If files exist, hash all files and assert they
are unchanged.

- [ ] **Step 3: Run the complete suite and hygiene checks**

```powershell
py -B -m unittest discover -s tests -v
git diff --check
git status --short
```

Expected: all tests pass, no whitespace errors, and no unrelated changes.

- [ ] **Step 4: Request final code review**

Review the implementation range against this plan and resolve any confirmed
Critical or Important findings before integration.
