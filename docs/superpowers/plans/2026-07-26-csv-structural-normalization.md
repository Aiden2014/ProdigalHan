# CSV Structural Normalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the CSV translation migration script with a deterministic structural fallback that matches ellipsis, asterisk line-break, and whitespace-only formatting changes while leaving semantic changes unmatched.

**Architecture:** Preserve the current case-folded exact lookup as Stage 1. For only the rows left unmatched, build a Stage 2 lookup from unused old-row indices using a narrowly scoped normalization helper; accept a normalized key only when it identifies exactly one old row. Keep exact and normalized counts separate while retaining the existing transactional write pipeline unchanged.

**Tech Stack:** Python 3 standard library (`re`, `dataclasses`, `pathlib`, `unittest`); existing CSV migration and transactional output code.

## Global Constraints

- Match the complete first-column key; never match on the second-column display text.
- Run case-insensitive exact matching before structural normalization.
- Normalize only ellipsis formatting, `*` line-break markers, and whitespace.
- Preserve every other punctuation mark, word, number, and character order.
- Do not add fuzzy matching, similarity scoring, edit distance, or semantic substitutions.
- Keep exact case-folded duplicate old keys as fatal validation errors.
- Treat normalized old-key collisions as nonfatal ambiguity and leave affected rows unmatched.
- Track old rows by index so duplicate row values and repeated new keys are accounted for correctly.
- Preserve existing discovery, `-ALLCH` exclusion, validation, UTF-8 BOM, atomic replacement, backup, rollback, and recovery behavior.
- Run every Python command with `py`, as requested by the user.

## File Structure

- Modify `scripts/merge_translation_csv.py`: structural key normalization, two-stage row matching, per-stage counts, and CLI summaries.
- Modify `tests/test_merge_translation_csv.py`: regression tests for approved normalization and rejected semantic/ambiguous matches.

---

### Task 1: Specify structural normalization and two-stage behavior with failing tests

**Files:**
- Modify: `tests/test_merge_translation_csv.py`
- Test: `tests/test_merge_translation_csv.py`

**Interfaces:**
- Import: `normalize_structural_key(value: str) -> str`
- Observe: `MigrationSummary.exact`, `MigrationSummary.normalized`, and compatibility property `MigrationSummary.matched`
- Observe CLI counters: `exact`, `normalized`, `old_only`, and `new_only`

- [ ] **Step 1: Import the normalization helper in the tests**

Change the existing import to:

```python
from scripts.merge_translation_csv import (
    MigrationError,
    main,
    migrate,
    normalize_structural_key,
)
```

- [ ] **Step 2: Add a direct normalization contract test**

Add this test to `MigrationTests`:

```python
def test_structural_normalization_unifies_ellipsis_stars_and_whitespace(self) -> None:
    expected = "shopitem-purchase--... i'm concerned."

    self.assertEqual(
        normalize_structural_key("ShopItem-Purchase--. . .*I'M CONCERNED."),
        expected,
    )
    self.assertEqual(
        normalize_structural_key("ShopItem-Purchase--...*I'm   concerned."),
        expected,
    )
    self.assertEqual(
        normalize_structural_key("ShopItem-Purchase--…  I'm concerned."),
        expected,
    )
```

This locks the normalization order to case-folding, ellipsis unification, `*` replacement, and whitespace collapse.

- [ ] **Step 3: Add a real ellipsis migration test with separate counters**

```python
def test_structural_match_migrates_ellipsis_variant(self) -> None:
    old_key = "ShopItem-Purchase--. . .*I'M CONCERNED."
    new_key = "ShopItem-Purchase--...*I'm concerned."
    write_csv(
        self.resources / "item.csv",
        [[old_key, old_key, "…*我很担心。"]],
    )
    write_csv(
        self.resources / "item-24023703.csv",
        [[new_key, new_key]],
    )

    summary = migrate(self.resources)

    self.assertEqual(
        read_csv(self.resources / "item-24023703.csv"),
        [[new_key, new_key, "…*我很担心。"]],
    )
    self.assertEqual((summary.exact, summary.normalized), (0, 1))
    self.assertEqual(summary.matched, 1)
    self.assertEqual((summary.old_only, summary.new_only), (0, 0))
```

- [ ] **Step 4: Update the obsolete star/whitespace significance test**

Replace `test_casefold_matching_keeps_whitespace_punctuation_and_stars_significant` with a test proving that moved stars and collapsed whitespace match, while unrelated punctuation remains significant:

```python
def test_structural_matching_ignores_stars_but_keeps_other_punctuation(self) -> None:
    old_rows = [
        ["SPACE*KEY", "Text", "space"],
        ["PUNCT-KEY", "Text", "punctuation"],
    ]
    new_rows = [
        ["space   key", "Text"],
        ["punct.key", "Text"],
    ]
    write_csv(self.resources / "speech.csv", old_rows)
    write_csv(self.resources / "speech-24023703.csv", new_rows)

    summary = migrate(self.resources)

    self.assertEqual((summary.exact, summary.normalized), (0, 1))
    self.assertEqual((summary.old_only, summary.new_only), (1, 1))
    self.assertEqual(
        read_csv(self.resources / "speech-24023703.csv"),
        [["space   key", "Text", "space"], ["punct.key", "Text", ""]],
    )
    self.assertEqual(
        read_csv(self.resources / "old" / "speech.csv"),
        [["PUNCT-KEY", "Text", "punctuation"]],
    )
    self.assertEqual(
        read_csv(self.resources / "new" / "speech-24023703.csv"),
        [["punct.key", "Text"]],
    )
```

- [ ] **Step 5: Add exact-precedence, ambiguity, and semantic-change tests**

Add these three tests:

```python
def test_exact_match_takes_precedence_over_structural_candidate(self) -> None:
    write_csv(
        self.resources / "speech.csv",
        [
            ["CTX-HELLO*WORLD", "Text", "exact"],
            ["CTX-HELLO WORLD", "Text", "fallback"],
        ],
    )
    write_csv(
        self.resources / "speech-24023703.csv",
        [["ctx-hello*world", "Text"]],
    )

    summary = migrate(self.resources)

    self.assertEqual(
        read_csv(self.resources / "speech-24023703.csv"),
        [["ctx-hello*world", "Text", "exact"]],
    )
    self.assertEqual((summary.exact, summary.normalized), (1, 0))
    self.assertEqual(summary.old_only, 1)

def test_normalized_old_key_collision_stays_unmatched(self) -> None:
    old_rows = [
        ["CTX-. . .*READY", "Text", "first"],
        ["CTX-… READY", "Text", "second"],
    ]
    new_rows = [["ctx-... ready", "Text"]]
    write_csv(self.resources / "speech.csv", old_rows)
    write_csv(self.resources / "speech-24023703.csv", new_rows)

    summary = migrate(self.resources)

    self.assertEqual((summary.exact, summary.normalized), (0, 0))
    self.assertEqual((summary.old_only, summary.new_only), (2, 1))
    self.assertEqual(
        read_csv(self.resources / "speech-24023703.csv"),
        [["ctx-... ready", "Text", ""]],
    )
    self.assertEqual(read_csv(self.resources / "old" / "speech.csv"), old_rows)
    self.assertEqual(
        read_csv(self.resources / "new" / "speech-24023703.csv"),
        new_rows,
    )

def test_semantic_you_to_i_change_stays_unmatched(self) -> None:
    old_key = (
        "ShopItem-Purchase--CLEATED BOOTS*EQUIPPED!"
        "*YOU WILL NOW DEAL*MORE DAMAGE."
    )
    new_key = (
        "ShopItem-Purchase--Cleated Boots equipped!"
        "*I will now deal more damage."
    )
    write_csv(
        self.resources / "item.csv",
        [[old_key, old_key, "防滑钉鞋*已装备！*你现在可以造成*更多的伤害。"]],
    )
    write_csv(
        self.resources / "item-24023703.csv",
        [[new_key, new_key]],
    )

    summary = migrate(self.resources)

    self.assertEqual((summary.exact, summary.normalized), (0, 0))
    self.assertEqual((summary.old_only, summary.new_only), (1, 1))
    self.assertEqual(
        read_csv(self.resources / "item-24023703.csv"),
        [[new_key, new_key, ""]],
    )
```

- [ ] **Step 6: Update the CLI success-output test**

Extend its fixture to contain one exact and one normalized row, then assert both the per-file and total counters:

```python
write_csv(
    resources / "speech.csv",
    [["KEY", "Text", "译文"], ["SECOND*KEY", "Text", "第二条"]],
)
write_csv(
    resources / "speech-24023703.csv",
    [["key", "Text"], ["second key", "Text"]],
)

stdout = io.StringIO()
with contextlib.redirect_stdout(stdout):
    exit_code = main(["--resources-dir", str(resources)])

self.assertEqual(exit_code, 0)
self.assertIn(
    "speech-24023703.csv: exact=1 normalized=1 old_only=0 new_only=0",
    stdout.getvalue(),
)
self.assertIn(
    "files=1 exact=1 normalized=1 old_only=0 new_only=0",
    stdout.getvalue(),
)
```

- [ ] **Step 7: Run the focused tests and confirm the expected RED state**

Run:

```powershell
py -m unittest tests.test_merge_translation_csv -v
```

Expected: the suite fails because `normalize_structural_key`, `exact`, and `normalized` do not exist yet and structural variants are still unmatched. Transaction and validation tests should not reveal unrelated regressions.

---

### Task 2: Implement deterministic structural fallback and counters

**Files:**
- Modify: `scripts/merge_translation_csv.py`
- Test: `tests/test_merge_translation_csv.py`

**Interfaces:**
- Add: `normalize_structural_key(value: str) -> str`
- Change: `FilePlan` fields to `exact: int` and `normalized: int`, with computed `matched`
- Change: `MigrationSummary` fields to `exact: int` and `normalized: int`, with computed `matched`
- Preserve: `migrate(resources_dir: Path) -> MigrationSummary` and all write/transaction functions

- [ ] **Step 1: Add the structural normalization helper**

Add `import re` and place this helper after the dataclasses:

```python
def normalize_structural_key(value: str) -> str:
    normalized = value.casefold()
    normalized = re.sub(r"\.\s*\.\s*\.", "...", normalized)
    normalized = normalized.replace("…", "...")
    normalized = normalized.replace("*", " ")
    return " ".join(normalized.split())
```

Do not add any broader punctuation stripping or word replacement.

- [ ] **Step 2: Split exact and normalized counters without breaking total-count callers**

Replace the count fields in both dataclasses and add read-only compatibility properties:

```python
@dataclass(frozen=True)
class FilePlan:
    old_path: Path
    new_path: Path
    migrated_rows: list[list[str]]
    unmatched_old_rows: list[list[str]]
    unmatched_new_rows: list[list[str]]
    exact: int
    normalized: int

    @property
    def matched(self) -> int:
        return self.exact + self.normalized


@dataclass(frozen=True)
class MigrationSummary:
    files: int
    exact: int
    normalized: int
    old_only: int
    new_only: int

    @property
    def matched(self) -> int:
        return self.exact + self.normalized
```

- [ ] **Step 3: Replace `build_file_plan` matching with indexed two-stage matching**

Keep existing reading and validation, then use old row indices as identities:

```python
translations: dict[str, tuple[int, str]] = {}
for old_index, row in enumerate(old_rows):
    key = row[0].casefold()
    if key in translations:
        raise MigrationError(
            f"{old_path}:{old_index + 1} duplicates first-column key "
            f"{row[0]!r} after case-folding"
        )
    translations[key] = (old_index, row[2] if len(row) >= 3 else "")

migrated_rows = [[row[0], row[1], ""] for row in new_rows]
unmatched_new_indices = []
used_old_indices: set[int] = set()
exact = 0

for new_index, row in enumerate(new_rows):
    match = translations.get(row[0].casefold())
    if match is None:
        unmatched_new_indices.append(new_index)
        continue
    old_index, translation = match
    migrated_rows[new_index][2] = translation
    used_old_indices.add(old_index)
    exact += 1

normalized_candidates: dict[str, list[tuple[int, str]]] = {}
for old_index, row in enumerate(old_rows):
    if old_index in used_old_indices:
        continue
    normalized_key = normalize_structural_key(row[0])
    translation = row[2] if len(row) >= 3 else ""
    normalized_candidates.setdefault(normalized_key, []).append(
        (old_index, translation)
    )

normalized_lookup = {
    key: candidates[0]
    for key, candidates in normalized_candidates.items()
    if len(candidates) == 1
}

still_unmatched_new_indices = []
normalized = 0
for new_index in unmatched_new_indices:
    match = normalized_lookup.get(normalize_structural_key(new_rows[new_index][0]))
    if match is None:
        still_unmatched_new_indices.append(new_index)
        continue
    old_index, translation = match
    migrated_rows[new_index][2] = translation
    used_old_indices.add(old_index)
    normalized += 1

unmatched_old_rows = [
    row.copy()
    for old_index, row in enumerate(old_rows)
    if old_index not in used_old_indices
]
unmatched_new_rows = [
    new_rows[new_index].copy()
    for new_index in still_unmatched_new_indices
]
```

Return `FilePlan(..., exact=exact, normalized=normalized)`. Do not remove a unique Stage 2 candidate after use; repeated new rows are intentionally allowed to reuse the same old translation.

- [ ] **Step 4: Aggregate and print separate counters**

Update `migrate_plans`:

```python
return MigrationSummary(
    files=len(plans),
    exact=sum(plan.exact for plan in plans),
    normalized=sum(plan.normalized for plan in plans),
    old_only=sum(len(plan.unmatched_old_rows) for plan in plans),
    new_only=sum(len(plan.unmatched_new_rows) for plan in plans),
)
```

Update the per-file and final CLI lines to:

```python
print(
    f"{plan.new_path.name}: exact={plan.exact} "
    f"normalized={plan.normalized} "
    f"old_only={len(plan.unmatched_old_rows)} "
    f"new_only={len(plan.unmatched_new_rows)}"
)

print(
    f"files={summary.files} exact={summary.exact} "
    f"normalized={summary.normalized} "
    f"old_only={summary.old_only} new_only={summary.new_only}"
)
```

- [ ] **Step 5: Run the focused suite and confirm GREEN**

Run:

```powershell
py -m unittest tests.test_merge_translation_csv -v
```

Expected: all 23 tests pass, including the five new tests, the updated structural/CLI tests, and all existing transactional tests.

- [ ] **Step 6: Run syntax compilation**

Run:

```powershell
py -m py_compile scripts\merge_translation_csv.py tests\test_merge_translation_csv.py
```

Expected: exit code 0 with no output.

- [ ] **Step 7: Commit the implementation**

Stage only the two implementation files and commit:

```powershell
git add scripts/merge_translation_csv.py tests/test_merge_translation_csv.py
git commit -m "feat(csv): normalize structural key formatting"
```

---

### Task 3: Verify current resources without writing them

**Files:**
- Read: `resources/**/*.csv`
- Verify: `scripts/merge_translation_csv.py`
- Verify: `tests/test_merge_translation_csv.py`

- [ ] **Step 1: Run a read-only real-resource preflight with hash protection**

Call `plan_migration()` only, hash every current resource file before and after, and print the aggregate counters:

```powershell
py -c "import hashlib; from pathlib import Path; from scripts.merge_translation_csv import plan_migration; root=Path('resources'); files=sorted(p for p in root.rglob('*') if p.is_file()); before={p:hashlib.sha256(p.read_bytes()).digest() for p in files}; plans=plan_migration(root); after={p:hashlib.sha256(p.read_bytes()).digest() for p in files}; assert before == after; print(f'files={len(plans)} exact={sum(p.exact for p in plans)} normalized={sum(p.normalized for p in plans)} old_only={sum(len(p.unmatched_old_rows) for p in plans)} new_only={sum(len(p.unmatched_new_rows) for p in plans)} hashes_unchanged={len(before)}')"
```

Expected on the currently scanned files: `normalized=783`, zero hash changes, and the exact/old/new totals printed for review. If resource inputs changed after this plan was written, investigate any count difference rather than weakening tests.

- [ ] **Step 2: Run the complete repository test discovery**

Run:

```powershell
py -m unittest discover -s tests -v
```

Expected: all tests pass.

- [ ] **Step 3: Inspect the final diff and whitespace health**

Run:

```powershell
git diff --check
git status --short
git diff -- scripts/merge_translation_csv.py tests/test_merge_translation_csv.py
```

Expected: no whitespace errors; only the intended script/test changes plus any pre-existing unrelated user changes. Do not stage or modify `Plugin.cs`, `StringDumper.cs`, `.claude/`, `scripts/copy_resources.py`, or `scripts/extract_uppercase.py`.

- [ ] **Step 4: Request code review before reporting completion**

Use `superpowers:requesting-code-review` against the implementation commit and address any confirmed issue through `superpowers:receiving-code-review`. Re-run Steps 1–3 after every fix.
