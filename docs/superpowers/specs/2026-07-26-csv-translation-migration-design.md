# CSV Translation Migration Design

## Goal

Add a Python script under `scripts/` that migrates third-column translations
from the previous game-version CSV files into the CSV files extracted from
Steam build `24023703`. Matching is based on the first column and ignores case.

## Inputs and File Pairing

The script scans `resources/*-24023703.csv`. For each new file, the previous
version is the file in the same directory whose name has `-24023703` removed.
For example:

- New: `resources/speech-24023703.csv`
- Previous: `resources/speech.csv`

Files ending in `-ALLCH.csv` are not inputs to this migration. Existing files
outside the paired base and build-specific CSV files are left untouched.

Both previous and new rows must contain at least two columns. A previous row
without a third column is valid and has an empty translation.

## Matching Rules

The first-column value is the lookup key. Keys are normalized only with
Python's `str.casefold()` method. Whitespace, punctuation, asterisks, and all
other characters remain significant.

Previous-version keys must be unique after normalization. If they are not,
the script reports the duplicate key and stops before writing any output.
Repeated normalized keys in a new-version file are allowed. Every repeated
row receives the translation associated with that key.

No row-number, second-column, or fuzzy fallback matching is performed.

## Data Flow and Outputs

The script first discovers, reads, and validates every file pair. It builds a
mapping from each normalized previous-version key to the previous row's third
column, using an empty string when that column is absent.

After all pairs pass validation, the script processes each new row:

1. If its normalized key exists in the previous mapping, set its third column
   to the mapped translation.
2. If it does not exist, set its third column to an empty string and record the
   original new row as unmatched.

The migrated rows overwrite the corresponding `*-24023703.csv` files. The
operation is idempotent: an existing third column is replaced rather than
appending a fourth column.

For every pair, the script also writes:

- Previous rows whose normalized keys do not occur in the new file to
  `resources/old/<previous filename>.csv`.
- New rows whose normalized keys do not occur in the previous file to
  `resources/new/<new filename>.csv`.

Unmatched reports preserve the original rows and column counts. A report with
no rows is still rewritten as an empty CSV so a prior run cannot leave stale
results. Other unrelated files already present in `resources/old` or
`resources/new` are not deleted.

All generated CSV files use UTF-8 with a byte-order mark and standard CSV
quoting. The script prints per-file and total matched/unmatched counts.

## Error Handling

Before changing files, the script rejects any of the following conditions:

- No `*-24023703.csv` input files are found.
- A corresponding previous-version file is missing.
- A row in either version has fewer than two columns.
- A previous-version file contains duplicate first-column keys after
  case-folding.
- A CSV cannot be decoded or parsed.

Validation errors produce a clear message and a nonzero exit status. Because
all validation precedes output, a validation failure leaves the CSV files
unchanged.

## Testing

Standard-library `unittest` tests use temporary resource directories and run
the real merge behavior. Coverage includes:

- First-column matching that differs only by case.
- Preservation of punctuation, quoted fields, and Chinese translations.
- Repeated new-version keys receiving the same translation.
- Old-only and new-only rows written to their respective reports.
- Missing third columns interpreted as empty translations.
- Existing new third columns replaced on repeated runs.
- Missing pairs, malformed rows, and duplicate previous keys failing before
  any output is written.

The implementation introduces no third-party dependencies.

## Out of Scope

- Migrating `-ALLCH.csv` translations.
- Fuzzy matching or normalization beyond case-folding.
- Automatically translating newly added game text.
- Changing the existing C# plugin or other resource-management scripts.
