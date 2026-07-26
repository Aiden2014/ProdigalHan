# CSV Generated State-Machine Index Matching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recover translation rows whose first-column keys differ only by Unity-generated `d__<number>` state-machine indices, while preserving ambiguity safety and reporting the new match category.

**Architecture:** Keep exact case-folded matching and existing structural normalization unchanged. Add a generated-index normalization stage before controlled fuzzy matching; it replaces `d__<digits>` with `d__#` for matching only, accepts only unique old candidates, and reports a separate generated counter.

**Tech Stack:** Python 3 standard library (`csv`, `dataclasses`, `pathlib`, `re`, `unittest`); existing CSV transaction and rollback implementation.

## Global Constraints

- Match only complete first-column keys; never use the second-column display text.
- Replace `d__<digits>` with `d__#` only in matching keys; preserve output row text exactly as read.
- Run stages in order: exact case-folded, structural normalization, generated-index normalization, then controlled fuzzy matching.
- Accept a generated-index match only when exactly one old row has the normalized key; ambiguity is nonfatal and remains unmatched.
- Repeated new rows may reuse the same unique old translation within the generated-index stage.
- Keep exact case-folded duplicate old keys fatal; keep generated-index and fuzzy ambiguity nonfatal.
- Do not perform semantic substitutions, broad punctuation stripping, unbounded nearest-neighbor matching, or arbitrary best-candidate selection.
- Preserve CSV discovery, `-ALLCH` exclusion, validation, UTF-8 BOM output, staging, backups, rollback, recovery-backup retention, and unmatched report locations.
- Keep the existing fuzzy constraints: same first-hyphen anchor, minimum normalized key length 20, edit distance at most 8, and similarity ratio at least 0.97 unless explicitly changed by the user at runtime.
- Run every Python command with `py` (use `py -B` for read-only verification where bytecode suppression matters).

---

### Task 1: Add failing generated-index matching tests

**Files:**
- Modify: `tests/test_merge_translation_csv.py`

**Interfaces:**
- Continue consuming `migrate(resources_dir: Path) -> MigrationSummary`.
- Add observable `MigrationSummary.generated: int` and preserve `matched == exact + normalized + generated + fuzzy`.

- [ ] **Step 1: Add a unique generated-index migration test**

Add a real CSV fixture where the old and new keys differ only by case,
punctuation normalization already supported by the script, and the generated
index:

```python
def test_generated_index_match_fills_translation(self) -> None:
    old_key = "Caroline/<MeetEvent>d__25-MoveNext-CAROLINE-HEY, YOU!*HELP ME OUT"
    new_key = "Caroline/<MeetEvent>d__26-MoveNext-Caroline-Hey, you!*Help me out"
    write_csv(self.resources / "speech.csv", [[old_key, "Old", "旧译文"]])
    write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

    summary = migrate(self.resources)

    self.assertEqual(read_csv(self.resources / "speech-24023703.csv"), [[new_key, "New", "旧译文"]])
    self.assertEqual((summary.exact, summary.normalized, summary.generated, summary.fuzzy), (0, 0, 1, 0))
    self.assertEqual((summary.old_only, summary.new_only), (0, 0))
```

- [ ] **Step 2: Add collision rejection and repeated-row tests**

Add one test with two old keys that both become the same `d__#` key and assert
the new row remains untranslated with `generated == 0`, `old_only == 2`, and
`new_only == 1`. Add another test with one unique old key and two new rows
that differ only by their generated index; assert both receive the translation
and `generated == 2`.

- [ ] **Step 3: Update CLI counter assertions**

Extend the existing CLI fixture and assertions so both per-file and total
lines include `generated=1` between `normalized` and `fuzzy`:

```python
self.assertIn(
    "speech-24023703.csv: exact=1 normalized=0 generated=1 fuzzy=0 old_only=0 new_only=0",
    stdout.getvalue(),
)
self.assertIn(
    "files=1 exact=1 normalized=0 generated=1 fuzzy=0 old_only=0 new_only=0",
    stdout.getvalue(),
)
```

- [ ] **Step 4: Run the focused suite and confirm RED**

Run:

```powershell
py -m unittest tests.test_merge_translation_csv -v
```

Expected: the new tests fail because `MigrationSummary.generated` and the
generated-index stage do not exist yet, while existing tests continue to show
the prior behavior.

- [ ] **Step 5: Commit the RED tests**

```powershell
git add tests/test_merge_translation_csv.py
git commit -m "test(csv): specify generated index matching"
```

---

### Task 2: Implement the generated-index stage and counters

**Files:**
- Modify: `scripts/merge_translation_csv.py`

**Interfaces:**
- Add `normalize_generated_index_key(value: str) -> str`.
- Add `FilePlan.generated: int` and `MigrationSummary.generated: int`.
- Preserve `migrate(resources_dir: Path) -> MigrationSummary` and update the
  `matched` compatibility properties.

- [ ] **Step 1: Add the matching-key helper**

After `normalize_structural_key`, add:

```python
GENERATED_INDEX_RE = re.compile(r"d__\d+", re.IGNORECASE)


def normalize_generated_index_key(value: str) -> str:
    return GENERATED_INDEX_RE.sub("d__#", normalize_structural_key(value))
```

- [ ] **Step 2: Extend counters and compatibility totals**

Add `generated: int` after `normalized` in both dataclasses and update:

```python
@property
def matched(self) -> int:
    return self.exact + self.normalized + self.generated + self.fuzzy
```

Update every constructor and aggregate summary to pass the new field.

- [ ] **Step 3: Add the unique generated-index matching stage**

After structural matching and before fuzzy candidate construction, build a
fixed candidate map from old rows not in `used_old_indices`:

```python
generated_candidates: dict[str, list[tuple[int, str]]] = {}
for old_index, row in enumerate(old_rows):
    if old_index in used_old_indices:
        continue
    key = normalize_generated_index_key(row[0])
    generated_candidates.setdefault(key, []).append(
        (old_index, row[2] if len(row) >= 3 else "")
    )

generated = 0
still_unmatched_after_generated: list[int] = []
for new_index in still_unmatched_new_indices:
    key = normalize_generated_index_key(new_rows[new_index][0])
    candidates = generated_candidates.get(key, [])
    if len(candidates) != 1:
        still_unmatched_after_generated.append(new_index)
        continue
    old_index, translation = candidates[0]
    migrated_rows[new_index][2] = translation
    used_old_indices.add(old_index)
    generated += 1
```

Pass `still_unmatched_after_generated` into the existing fuzzy stage. Keep the
candidate map fixed so repeated new rows can reuse a unique old row.

- [ ] **Step 4: Update fuzzy input, aggregation, and CLI output**

Use the post-generated unmatched list for fuzzy matching and update totals:

```python
return MigrationSummary(
    files=len(plans),
    exact=sum(plan.exact for plan in plans),
    normalized=sum(plan.normalized for plan in plans),
    generated=sum(plan.generated for plan in plans),
    fuzzy=sum(plan.fuzzy for plan in plans),
    old_only=sum(len(plan.unmatched_old_rows) for plan in plans),
    new_only=sum(len(plan.unmatched_new_rows) for plan in plans),
)
```

Print `generated={...}` between `normalized` and `fuzzy` in both per-file and
total CLI lines.

- [ ] **Step 5: Run focused tests and compilation GREEN**

```powershell
py -m unittest tests.test_merge_translation_csv -v
py -m py_compile scripts\merge_translation_csv.py tests\test_merge_translation_csv.py
```

Expected: all focused tests pass.

- [ ] **Step 6: Commit the implementation**

```powershell
git add scripts/merge_translation_csv.py tests/test_merge_translation_csv.py
git commit -m "feat(csv): match generated state-machine indices"
```

---

### Task 3: Verify real resources and migration safety

**Files:**
- Read: `resources/**/*.csv`
- Verify: `scripts/merge_translation_csv.py`
- Verify: `tests/test_merge_translation_csv.py`

- [ ] **Step 1: Run a bytecode-suppressed real-resource preflight**

Run `plan_migration()` only against `D:\projects\ProdigalHan\resources`,
compare SHA-256 hashes before and after, and print exact, normalized,
generated, fuzzy, old-only, and new-only totals. Do not run `migrate()` on the
real resource directory.

- [ ] **Step 2: Run the complete suite with bytecode suppression**

```powershell
py -B -m unittest discover -s tests -v
```

- [ ] **Step 3: Check diff hygiene**

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors and no unrelated file changes.

- [ ] **Step 4: Request final code review**

Review the implementation range against this plan and resolve any confirmed
Critical or Important findings before integration.
