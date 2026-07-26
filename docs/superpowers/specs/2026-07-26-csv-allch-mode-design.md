# CSV ALLCH Merge Mode

## Goal

Allow the translation migration script to choose between normal CSV pairs and
the `-ALLCH` CSV pairs without changing normal-mode behavior.

## Design

Expose an `allch: bool = False` parameter on `discover_pairs`,
`plan_migration`, `migration_outputs`, `migrate_plans`, and `migrate`. Add a
CLI `--allch` flag that passes `True`.

Normal mode keeps the current file selection:

- old: `<name>.csv`
- new: `<name>-24023703.csv`
- unmatched reports: `resources/old` and `resources/new`

ALLCH mode selects:

- old: `<name>-ALLCH.csv`
- new: `<name>-ALLCH-24023703.csv`
- unmatched reports: `resources/old-allch` and `resources/new-allch`

The merged output is written to the selected new path, so ALLCH output keeps
the `-ALLCH-24023703.csv` filename. Existing translation matching stages,
generated-index normalization, fuzzy thresholds, validation, and transaction
behavior are shared by both modes.

## Safety

- Normal mode continues excluding ALLCH files from discovery.
- ALLCH mode only pairs files with the exact ALLCH suffix and never mixes them
  with normal files.
- Report directories are selected explicitly by mode and are created only by
  the existing output staging path.
- The first-column matching rules and original row text remain unchanged.

## Verification

Add real CSV tests for normal-mode compatibility, ALLCH pairing and output
naming, mode-specific report directories, CLI `--allch`, and missing-pair
validation. Run the full suite and a read-only ALLCH preflight without
migrating real resources.
