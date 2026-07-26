# CSV Controlled Fuzzy Matching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a BOM-safe, conservative third matching stage that recovers unique small edits such as punctuation changes and short typos while keeping ambiguous or semantic changes unmatched.

**Architecture:** Preserve exact case-folded matching and structural normalization as the first two stages. Add reusable key preparation, stable-prefix candidate bucketing, bounded Levenshtein distance, and unique-candidate acceptance for a final fuzzy stage. Keep all matching in the in-memory plan before the existing transactional output path and report exact, normalized, and fuzzy counts separately.

**Tech Stack:** Python 3 standard library (`csv`, `dataclasses`, `pathlib`, `unittest`); existing CSV transaction and rollback implementation.

## Global Constraints

- Match only complete first-column keys; never use the second-column display text.
- Remove all leading `\ufeff` characters from matching keys, but preserve output row text exactly as read.
- Run stages in order: exact case-folded, structural normalization, then controlled fuzzy matching.
- Structural normalization remains limited to ellipsis formatting, `*` line-break markers, and whitespace.
- Fuzzy candidates must share the exact prefix before the first ASCII `-` after structural normalization.
- A fuzzy candidate requires minimum normalized key length 20, edit distance at most 8, and similarity ratio at least 0.97.
- Accept a fuzzy match only when exactly one unused old row passes all candidate rules; ambiguity is nonfatal and remains unmatched.
- Repeated new rows may reuse the same unique old translation within a matching stage.
- Do not perform semantic substitutions, broad punctuation stripping, unbounded nearest-neighbor matching, or arbitrary best-candidate selection.
- Keep exact case-folded duplicate old keys fatal; keep normalized/fuzzy ambiguity nonfatal.
- Preserve CSV discovery, `-ALLCH` exclusion, validation, UTF-8 BOM output, staging, backups, rollback, recovery-backup retention, and unmatched report locations.
- Run every Python command with `py` (use `py -B` for read-only verification where bytecode suppression matters).

## File Structure

- Modify `scripts/merge_translation_csv.py`: BOM-safe key preparation, bounded distance, fuzzy matching, counters, and CLI output.
- Modify `tests/test_merge_translation_csv.py`: real CSV behavior tests for fuzzy acceptance/rejection and updated counters.

---

### Task 1: Specify BOM and controlled fuzzy behavior with failing tests

**Files:**
- Modify: `tests/test_merge_translation_csv.py`
- Test: `tests/test_merge_translation_csv.py`

**Interfaces:**
- Continue consuming `migrate(resources_dir: Path) -> MigrationSummary`.
- New observable field: `MigrationSummary.fuzzy: int`.
- Preserve `MigrationSummary.matched`, now equal to exact + normalized + fuzzy.

- [ ] **Step 1: Add a double-BOM first-column migration test**

Add this test to `MigrationTests`. It deliberately writes two BOM sequences
to the old file and verifies that only the matching key is cleaned; the
visible output key remains the new row's original text:

```python
def test_matching_ignores_extra_leading_bom_in_old_key(self) -> None:
    old_path = self.resources / "speech.csv"
    write_csv(
        old_path,
        [["KEY-BOM-CONTEXT-LONG", "Old display", "旧译文"]],
        encoding="utf-8-sig",
    )
    old_path.write_bytes(b"\xef\xbb\xbf" + old_path.read_bytes())
    new_rows = [["key-bom-context-long", "New display"]]
    write_csv(self.resources / "speech-24023703.csv", new_rows)

    summary = migrate(self.resources)

    self.assertEqual(
        read_csv(self.resources / "speech-24023703.csv"),
        [["key-bom-context-long", "New display", "旧译文"]],
    )
    self.assertEqual((summary.exact, summary.normalized, summary.fuzzy), (1, 0, 0))
    self.assertEqual((summary.old_only, summary.new_only), (0, 0))
```

- [ ] **Step 2: Add a punctuation-only fuzzy-match test**

```python
def test_fuzzy_matching_accepts_small_punctuation_edit(self) -> None:
    old_key = (
        "ELITE_FEAT-CHECK-AURA-INCREDIBLE. . .*I.*IS THERE REALLY A"
        "*CHANCE?*THERE MAY BE A*WAY.*LISTEN CLOSE TO*THIS FORTUNE."
    )
    new_key = (
        "ELITE_FEAT-CHECK-Aura-Incredible...*I-*Is there really a chance?"
        "*There may be a way.*Listen close to this fortune."
    )
    write_csv(self.resources / "speech.csv", [[old_key, "Old", "旧占卜"]])
    write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

    summary = migrate(self.resources)

    self.assertEqual(
        read_csv(self.resources / "speech-24023703.csv"),
        [[new_key, "New", "旧占卜"]],
    )
    self.assertEqual((summary.exact, summary.normalized, summary.fuzzy), (0, 0, 1))
    self.assertEqual((summary.old_only, summary.new_only), (0, 0))
```

- [ ] **Step 3: Add a short-spelling-edit fuzzy-match test**

```python
def test_fuzzy_matching_accepts_unique_short_spelling_edit(self) -> None:
    old_key = "SPELL-CONTEXT-A LONG SPELLING MISTAKE IN THIS MESSAGE"
    new_key = "SPELL-CONTEXT-A LONG SPELING MISTAKE IN THIS MESSAGE"
    write_csv(self.resources / "speech.csv", [[old_key, "Old", "拼写译文"]])
    write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

    summary = migrate(self.resources)

    self.assertEqual(
        read_csv(self.resources / "speech-24023703.csv"),
        [[new_key, "New", "拼写译文"]],
    )
    self.assertEqual((summary.exact, summary.normalized, summary.fuzzy), (0, 0, 1))
```

- [ ] **Step 4: Add minimum-length and ambiguity rejection tests**

Add both tests below. The first proves a short key is never fuzzy-matched;
the second proves two qualifying old candidates stay visible as unmatched:

```python
def test_fuzzy_matching_rejects_keys_shorter_than_minimum_length(self) -> None:
    old_key = "A-123456789012"
    new_key = "A-123456789013"
    write_csv(self.resources / "item.csv", [[old_key, "Old", "不应匹配"]])
    write_csv(self.resources / "item-24023703.csv", [[new_key, "New"]])

    summary = migrate(self.resources)

    self.assertEqual(
        read_csv(self.resources / "item-24023703.csv"),
        [[new_key, "New", ""]],
    )
    self.assertEqual((summary.fuzzy, summary.old_only, summary.new_only), (0, 1, 1))

def test_fuzzy_matching_rejects_ambiguous_candidates(self) -> None:
    old_rows = [
        ["AMBIG-CONTEXT-A LONG MESSAGE WITH ONE", "Old A", "甲"],
        ["AMBIG-CONTEXT-B LONG MESSAGE WITH ONE", "Old B", "乙"],
    ]
    new_key = "AMBIG-CONTEXT-C LONG MESSAGE WITH ONE"
    write_csv(self.resources / "speech.csv", old_rows)
    write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

    summary = migrate(self.resources)

    self.assertEqual(
        read_csv(self.resources / "speech-24023703.csv"),
        [[new_key, "New", ""]],
    )
    self.assertEqual((summary.fuzzy, summary.old_only, summary.new_only), (0, 2, 1))
    self.assertEqual(read_csv(self.resources / "old" / "speech.csv"), old_rows)
    self.assertEqual(
        read_csv(self.resources / "new" / "speech-24023703.csv"),
        [[new_key, "New"]],
    )
```

- [ ] **Step 5: Update the existing semantic-change and CLI tests**

Keep the Cleated Boots `YOU` versus `I` fixture unmatched and add the fuzzy
counter to its assertion:

```python
self.assertEqual((summary.exact, summary.normalized, summary.fuzzy), (0, 0, 0))
```

Extend `CliTests.test_main_reports_totals` with a third old/new row pair using
the old key `FUZZY-CONTEXT-A LONG SPELLING MISTAKE IN THIS MESSAGE` and the new
key `FUZZY-CONTEXT-A LONG SPELING MISTAKE IN THIS MESSAGE`. Update both CLI
assertions to include `fuzzy=1`:

```python
self.assertIn(
    "speech-24023703.csv: exact=1 normalized=1 fuzzy=1 old_only=0 new_only=0",
    stdout.getvalue(),
)
self.assertIn(
    "files=1 exact=1 normalized=1 fuzzy=1 old_only=0 new_only=0",
    stdout.getvalue(),
)
```

- [ ] **Step 6: Run the focused suite and confirm RED**

Run:

```powershell
py -m unittest tests.test_merge_translation_csv -v
```

Expected: the existing suite runs but the new tests fail because the current
script does not remove duplicate BOMs, does not have a fuzzy stage, does not
expose `fuzzy`, and still prints the old CLI format. No production code is
written before observing this failure.

- [ ] **Step 7: Commit the RED tests**

```powershell
git add tests/test_merge_translation_csv.py
git commit -m "test(csv): specify controlled fuzzy matching"
```

---

### Task 2: Implement BOM-safe keys, bounded distance, fuzzy stage, and counters

**Files:**
- Modify: `scripts/merge_translation_csv.py`
- Test: `tests/test_merge_translation_csv.py`

**Interfaces:**
- Add `prepare_matching_key(value: str) -> str`.
- Add `bounded_edit_distance(left: str, right: str, limit: int) -> int`.
- Add `fuzzy_anchor(value: str) -> str`.
- Add `fuzzy_similarity(left: str, right: str) -> float | None`.
- Add `FilePlan.fuzzy` and `MigrationSummary.fuzzy` integer fields.
- Preserve `migrate(resources_dir: Path) -> MigrationSummary` and the
  `matched` compatibility property.

- [ ] **Step 1: Add constants and matching-key helpers**

Add these constants and helpers after the dataclasses/imports:

```python
FUZZY_MIN_LENGTH = 20
FUZZY_MAX_EDIT_DISTANCE = 8
FUZZY_MIN_RATIO = 0.97


def prepare_matching_key(value: str) -> str:
    return value.lstrip("\ufeff")


def normalize_structural_key(value: str) -> str:
    normalized = prepare_matching_key(value).casefold()
    normalized = re.sub(r"\.\s*\.\s*\.", "...", normalized)
    normalized = normalized.replace("…", "...")
    normalized = normalized.replace("*", " ")
    return " ".join(normalized.split())


def fuzzy_anchor(value: str) -> str:
    return normalize_structural_key(value).split("-", 1)[0]
```

Update every exact-key lookup and duplicate check to use
`prepare_matching_key(row[0]).casefold()`. Do not alter row values written to
the migrated CSV or reports.

- [ ] **Step 2: Add bounded Levenshtein distance and ratio helpers**

Implement the standard-library-only helpers:

```python
def bounded_edit_distance(left: str, right: str, limit: int) -> int:
    if abs(len(left) - len(right)) > limit:
        return limit + 1
    previous = list(range(len(right) + 1))
    for left_index, left_char in enumerate(left, 1):
        current = [left_index]
        for right_index, right_char in enumerate(right, 1):
            current.append(
                min(
                    current[-1] + 1,
                    previous[right_index] + 1,
                    previous[right_index - 1] + (left_char != right_char),
                )
            )
        previous = current
    return previous[-1]


def fuzzy_similarity(left: str, right: str) -> float | None:
    if min(len(left), len(right)) < FUZZY_MIN_LENGTH:
        return None
    distance = bounded_edit_distance(left, right, FUZZY_MAX_EDIT_DISTANCE)
    if distance > FUZZY_MAX_EDIT_DISTANCE:
        return None
    return 1 - distance / max(len(left), len(right))
```

Return `None` for candidates that fail the minimum length or distance limit;
the caller will then apply the ratio threshold.

- [ ] **Step 3: Extend dataclasses and compatibility totals**

Add `fuzzy: int` after `normalized` in both dataclasses and update the
properties:

```python
@property
def matched(self) -> int:
    return self.exact + self.normalized + self.fuzzy
```

Update all constructors and aggregate summaries to provide the new field.

- [ ] **Step 4: Add the fuzzy stage after structural matching**

After the existing structural stage has collected `still_unmatched_new_indices`,
build candidate buckets from old rows not consumed by exact or structural
matching. Keep this candidate list fixed during the stage so repeated fuzzy
new rows can reuse the same unique old row:

```python
fuzzy_candidates: dict[str, list[tuple[int, str, str]]] = {}
for old_index, row in enumerate(old_rows):
    if old_index in used_old_indices:
        continue
    normalized_key = normalize_structural_key(row[0])
    fuzzy_candidates.setdefault(fuzzy_anchor(row[0]), []).append(
        (old_index, normalized_key, row[2] if len(row) >= 3 else "")
    )

fuzzy = 0
still_unmatched_after_fuzzy: list[int] = []
for new_index in still_unmatched_new_indices:
    new_key = normalize_structural_key(new_rows[new_index][0])
    candidates = []
    for old_index, old_key, translation in fuzzy_candidates.get(
        fuzzy_anchor(new_rows[new_index][0]), []
    ):
        similarity = fuzzy_similarity(old_key, new_key)
        if similarity is not None and similarity >= FUZZY_MIN_RATIO:
            candidates.append((old_index, translation))
    if len(candidates) != 1:
        still_unmatched_after_fuzzy.append(new_index)
        continue
    old_index, translation = candidates[0]
    migrated_rows[new_index][2] = translation
    used_old_indices.add(old_index)
    fuzzy += 1
```

Use `still_unmatched_after_fuzzy` to build `unmatched_new_rows`. Keep
`unmatched_old_rows` based on `used_old_indices` after all three stages.

- [ ] **Step 5: Update aggregation and CLI output**

Update `migrate_plans`:

```python
return MigrationSummary(
    files=len(plans),
    exact=sum(plan.exact for plan in plans),
    normalized=sum(plan.normalized for plan in plans),
    fuzzy=sum(plan.fuzzy for plan in plans),
    old_only=sum(len(plan.unmatched_old_rows) for plan in plans),
    new_only=sum(len(plan.unmatched_new_rows) for plan in plans),
)
```

Update per-file and total CLI lines to include `fuzzy` between normalized and
old-only counts:

```python
f"{plan.new_path.name}: exact={plan.exact} "
f"normalized={plan.normalized} fuzzy={plan.fuzzy} "
f"old_only={len(plan.unmatched_old_rows)} "
f"new_only={len(plan.unmatched_new_rows)}"
```

- [ ] **Step 6: Run focused tests and compilation GREEN**

```powershell
py -m unittest tests.test_merge_translation_csv -v
py -m py_compile scripts\merge_translation_csv.py tests\test_merge_translation_csv.py
```

Expected: all 29 tests pass and compilation exits 0 with no output.

- [ ] **Step 7: Commit implementation**

```powershell
git add scripts/merge_translation_csv.py tests/test_merge_translation_csv.py
git commit -m "feat(csv): add controlled fuzzy matching"
```

---

### Task 3: Verify real resources without migrating them

**Files:**
- Read: `resources/**/*.csv`
- Verify: `scripts/merge_translation_csv.py`
- Verify: `tests/test_merge_translation_csv.py`

- [ ] **Step 1: Run a bytecode-suppressed real-resource preflight**

Run `plan_migration()` only and compare resource hashes before and after:

```powershell
py -B -c "import hashlib; from pathlib import Path; from scripts.merge_translation_csv import plan_migration; root=Path(r'D:\projects\ProdigalHan\resources'); files=sorted(p for p in root.rglob('*') if p.is_file()); before={p:hashlib.sha256(p.read_bytes()).digest() for p in files}; plans=plan_migration(root); after={p:hashlib.sha256(p.read_bytes()).digest() for p in files}; assert before == after; print(f'files={len(plans)} exact={sum(p.exact for p in plans)} normalized={sum(p.normalized for p in plans)} fuzzy={sum(p.fuzzy for p in plans)} old_only={sum(len(p.unmatched_old_rows) for p in plans)} new_only={sum(len(p.unmatched_new_rows) for p in plans)} hashes_unchanged={len(before)}')"
```

Expected: the command exits 0, prints all five counters, and reports equal
before/after hashes. The migration command must not be run against the real
resource directory.

- [ ] **Step 2: Run complete tests with bytecode suppression**

```powershell
py -B -m unittest discover -s tests -v
```

Expected: all 29 tests pass.

- [ ] **Step 3: Check final diff and workspace hygiene**

```powershell
git diff --check
git status --short
git diff -- scripts/merge_translation_csv.py tests/test_merge_translation_csv.py
```

Expected: no whitespace errors; only intended script/test/design/plan commits,
with unrelated user files left untouched.

- [ ] **Step 4: Request final code review**

Use `superpowers:requesting-code-review` against the implementation range,
then resolve any confirmed Critical or Important findings before reporting
completion.

