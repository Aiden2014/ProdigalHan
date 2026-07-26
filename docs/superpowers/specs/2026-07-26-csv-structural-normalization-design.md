# CSV Structural Normalization Design

## Goal

Extend `scripts/merge_translation_csv.py` with a deterministic second matching
stage so game text that differs only in ellipsis formatting, asterisk line
break positions, or surrounding whitespace can inherit its previous
translation.

The existing case-insensitive exact-key stage remains the first and preferred
match. Fuzzy matching and semantic rewriting are explicitly excluded.

## Observed Data

The Steam build changed some formatting without changing the text content:

- `. . .` became `...`.
- `…` and three-dot ellipses occur in equivalent positions.
- `*` line-break markers moved or disappeared.
- Removing a `*` can expose different amounts of whitespace.

A read-only scan of the current resources found that structural normalization
would add 783 matches across the extracted build files, with no normalized-key
ambiguities in the current data.

The Cleated Boots example also changes `YOU` to `I`. That is a semantic text
change, not formatting. It must remain unmatched under this design.

## Matching Pipeline

### Stage 1: Exact case-insensitive key

Match the complete first column with `str.casefold()`, exactly as the current
script does. Existing duplicate-old-key validation and repeated-new-key
behavior remain unchanged.

### Stage 2: Structurally normalized key

Apply this stage only to rows that Stage 1 did not match. Normalize the entire
first-column key in this exact order:

1. Apply `str.casefold()`.
2. Convert a three-dot ellipsis whose dots are separated by any whitespace to
   `...`.
3. Convert the single Unicode ellipsis character `…` to `...`.
4. Replace every `*` with one ASCII space.
5. Collapse every run of whitespace to one ASCII space and remove leading or
   trailing whitespace.

Do not remove or rewrite any other punctuation, words, numbers, or character
order.

Build the normalized lookup only from previous-version rows that were not
already consumed by Stage 1. A normalized old key is eligible only when it
identifies exactly one old row. If two or more old rows produce the same
normalized key, the key is ambiguous and no Stage 2 match is made for it.

Each new row matched by Stage 2 receives the unique old row's third-column
translation. Repeated new rows with the same structurally normalized key may
receive the same translation, matching the existing repeated-new-key behavior.

## Matched and Unmatched Accounting

Track the specific old row used by either matching stage. An old row is
`old_only` only when neither Stage 1 nor Stage 2 used it. A new row is
`new_only` only when neither stage matched it.

Per-file and total CLI summaries report these counters separately:

- `exact`
- `normalized`
- `old_only`
- `new_only`

The output CSV schema, UTF-8 BOM encoding, atomic staging, backup, rollback,
and recovery behavior do not change. Reports under `resources/old` and
`resources/new` are generated only after both matching stages finish.

## Error and Ambiguity Handling

Normalized collisions are not fatal input errors because they affect only the
optional fallback stage. They are treated as unmatched and remain visible in
the old/new reports. Exact case-folded duplicate old keys remain fatal, as in
the current implementation.

The script performs no similarity scoring, edit-distance matching, word
substitution, pronoun substitution, or other semantic inference.

## Testing

Add standard-library `unittest` coverage for:

- `. . .`, `...`, and `…` equivalence.
- Moved or removed `*` line-break markers.
- Whitespace collapse around replaced line-break markers.
- Exact matches taking precedence over normalized matches.
- Two old keys colliding after structural normalization and therefore staying
  unmatched.
- Semantic changes such as `YOU` versus `I` remaining unmatched.
- Correct `exact`, `normalized`, `old_only`, and `new_only` counts.
- Existing transactional output, rollback, BOM, validation, CLI error, and
  `-ALLCH` tests remaining green.

Run the real-resource check through `plan_migration()` only and compare all
resource file hashes before and after so verification cannot alter extracted
CSV files.

## Out of Scope

- Fuzzy or similarity-based matching.
- Semantic equivalence or word substitutions.
- Manual candidate-review files.
- Changes to CSV pairing, validation, encoding, transaction, backup, rollback,
  or CLI error behavior.
