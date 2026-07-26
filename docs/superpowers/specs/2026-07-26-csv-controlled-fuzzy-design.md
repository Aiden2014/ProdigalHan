# CSV Controlled Fuzzy Matching Design

## Goal

Extend `scripts/merge_translation_csv.py` with a conservative third matching
stage for rows that remain unmatched after exact case-insensitive matching and
structural formatting normalization. The stage should recover small text
edits such as a punctuation substitution or a short spelling typo without
automatically pairing substantially rewritten story text.

## Existing Matching Pipeline

The migration currently matches complete first-column keys in this order:

1. Exact `casefold()` matching.
2. Structural normalization of ellipses, `*` line-break markers, and
   whitespace.

The new stage runs only for rows still unmatched after both existing stages.
It must not change CSV discovery, validation, transaction, BOM output, or
unmatched-report destinations.

## BOM-Safe Key Preparation

The old extracted CSV files can contain two UTF-8 BOM sequences. Reading with
`utf-8-sig` removes one sequence but can leave a leading `\ufeff` in the first
column. Every matching-key preparation path will remove all leading `\ufeff`
characters before case-folding. The output rows themselves remain unchanged.

This cleanup applies to exact, structural, and fuzzy matching, so a duplicate
old key created only by extra BOM characters is not treated as a distinct key.

## Controlled Fuzzy Stage

### Candidate partition

For each unmatched new row, compare only unused old rows whose prepared,
structurally normalized key has the same prefix before the first ASCII `-`.
The prefix comparison is case-insensitive through the prepared normalized key.
This context anchor prevents unrelated CSV keys with similar prose from being
compared indiscriminately.

### Acceptance rules

A candidate is eligible only when all rules hold:

- The structurally normalized key length is at least 20 characters.
- The bounded Levenshtein edit distance is at most 8.
- The normalized similarity ratio is at least `0.97`.
- Exactly one unused old row passes all rules for that new row.

The similarity ratio is computed as:

```text
1 - edit_distance / max(len(old_key), len(new_key))
```

An edit-distance calculation may stop early once the distance exceeds 8. A
candidate that fails any rule is ignored. If two or more old rows pass, the
new row remains unmatched; no arbitrary best-candidate selection is made.

### Row reuse and accounting

Repeated new rows may reuse the same unique old row, matching the existing
exact and structural behavior. The old row is considered used after its first
successful fuzzy match, and old-only/new-only reports are computed after all
three stages. A normalized collision remains ineligible for fuzzy matching if
its old rows do not yield a unique accepted candidate.

Fuzzy matching does not perform word substitutions, pronoun changes, semantic
rewrites, or broad punctuation stripping. A change such as `YOU` to `I` or a
large story-text rewrite should remain unmatched.

## API and CLI Accounting

`FilePlan` and `MigrationSummary` gain a `fuzzy` count. The compatibility
`matched` property becomes `exact + normalized + fuzzy`.

Per-file and total CLI output report:

```text
exact=... normalized=... fuzzy=... old_only=... new_only=...
```

No fuzzy candidate-review file is generated. Rows that fail or are ambiguous
remain in the existing `resources/old` and `resources/new` reports.

## Error Handling and Safety

- Exact case-folded duplicate old keys remain fatal validation errors.
- Fuzzy ambiguity is nonfatal and visible through unmatched reports.
- Fuzzy matching runs entirely in the in-memory planning phase before any
  transactional write begins.
- Existing staging, backup, rollback, recovery-backup retention, UTF-8 BOM,
  and ALLCH exclusion behavior remain unchanged.

## Testing

Add standard-library `unittest` coverage for:

- Removing duplicate leading BOM characters from matching keys.
- Matching a punctuation-only `I.` versus `I-` change through the fuzzy stage.
- Matching a short spelling edit within the distance and ratio limits.
- Rejecting candidates shorter than 20 characters.
- Rejecting two old rows that both satisfy the fuzzy rules.
- Leaving a semantic rewrite such as `YOU` versus `I` unmatched.
- Reporting separate exact, normalized, fuzzy, old-only, and new-only counts.
- Preserving existing transaction, rollback, BOM, validation, CLI, and ALLCH
  tests.

Run the real-resource check through `plan_migration()` only, with all resource
file hashes compared before and after. The migration command itself must not
be run against the extracted resource directory during verification.

## Out of Scope

- Unbounded similarity or nearest-neighbor matching.
- Matching without a stable context prefix.
- Selecting an arbitrary candidate when multiple candidates qualify.
- Semantic word or pronoun substitution.
- Automatic edits to the original extracted resource files.
- A separate fuzzy-candidate report file.
