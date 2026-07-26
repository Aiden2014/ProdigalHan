# CSV Generated State-Machine Index Matching

## Goal

Recover translation rows whose first-column keys differ only because Unity
generated state-machine method numbers changed between builds, such as
`<MeetEvent>d__25` becoming `<MeetEvent>d__26`.

## Design

Add a dedicated matching stage after structural normalization and before the
existing controlled fuzzy stage. For matching only, replace every generated
state-machine index matching `d__<digits>` (case-insensitively) with the stable
token `d__#`. Original row keys and report rows remain unchanged.

Build old-row candidate buckets from rows not consumed by exact or structural
matching. A generated-index match is accepted only when the normalized key
maps to exactly one old row. Collisions remain unmatched rather than choosing
an arbitrary translation. The candidate list is fixed during this stage so
repeated new rows may reuse the same unique old translation.

Expose a separate `generated` counter in `FilePlan`, `MigrationSummary`, the
`matched` compatibility totals, and CLI output. The output order becomes
`exact`, `normalized`, `generated`, `fuzzy`, `old_only`, `new_only`.

## Safety

- Matching continues to use only the first CSV column.
- The original casefold and structural stages keep their existing behavior.
- Generated-index normalization is exact after replacement; it does not use
  semantic substitutions or broad fuzzy matching.
- Existing validation, transaction/rollback, UTF-8 BOM output, `-ALLCH`
  exclusion, and unmatched report locations remain unchanged.

## Verification

Add real CSV tests for a unique generated-index match, collision rejection,
repeated-new-row reuse, counters, and CLI output. Run the complete test suite
and a read-only real-resource preflight after implementation.
