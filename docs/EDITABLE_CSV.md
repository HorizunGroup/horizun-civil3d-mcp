# Editable COGO point exchange

`horizun_c3d_points` provides `export_editable_csv` and `apply_csv`.
The CSV is intended for editing in a spreadsheet, then applying changes to the
existing Civil COGO points. It does not create or delete points.

## Export, edit, rehearse and apply

1. Export with an explicit `target_document`, absolute new `.csv` `output`, and
   optional `group` or `numbers`. Export requires Full Write. Its default dry run
   returns a confirmation token; applying the same request writes and reopens the
   file to verify every byte. Existing destinations are refused.
2. Open/import as **UTF-8, comma separated, decimal point**. Import
   `source_sha256`, `linear_unit`, `number` and `description` as text where the
   spreadsheet allows it. Keep survey coordinate precision; formatting displayed
   decimals is separate from reducing stored values.
3. Keep every exported row and its first three fields. Edit only `easting`,
   `northing`, `elevation` and plain-text `description`. Quoted commas and quotes
   are supported; multiline descriptions and spreadsheet formulas are refused.
   Save CSV using commas and invariant decimals. A locale export using semicolons
   or decimal commas is refused; no separator or unit is guessed.
4. Rehearse `apply_csv` with the **same** `target_document` and selection
   (`group`/`numbers`), plus absolute `.csv` `file`. Applying requires Safe Write.
   Review each before/after value, then send the identical request with
   `dry_run=false` and the returned single-use token.
5. The add-in updates all resolved point coordinates/descriptions in one drawing
   transaction. It reopens every edited point in a new transaction and verifies
   number, easting, northing, elevation and raw description. No drawing is saved.
   Drawing UNDO is available when a drawing change was recorded.

Example rehearsal requests (generated fixture names only):

```json
{
  "action": "export_editable_csv",
  "target_document": "fixture.dwg",
  "numbers": "1-20",
  "output": "C:\\exchange\\points-edit.csv"
}
```

```json
{
  "action": "apply_csv",
  "target_document": "fixture.dwg",
  "numbers": "1-20",
  "file": "C:\\exchange\\points-edit.csv"
}
```

## Source and limits

The source hash binds the persistent drawing identity/name, exact selected point
numbers, all baseline coordinates/descriptions and the explicit Civil linear
unit (`meter`, international `foot`, or `USSurveyFoot`). A renamed/different
drawing, changed point selection, altered baseline geometry or changed units
requires exporting again. A changed source is refused before any drawing edit.
The confirmation token separately binds the latest drawing revision and the
entire CSV byte hash, so edits after rehearsal invalidate the approved request.
No coordinate translation, shared-coordinate adjustment or reprojection occurs.

Guards: 1–10,000 rows, 8 MiB UTF-8 CSV, 1,024 plain-text characters per
description, finite coordinates, exact seven-column header, unique existing
positive point numbers, no missing rows. Locked points, locked layers,
references and Civil objects reported non-editable are refused before writing.

The pure parser and real CLR tests are automated. Live Civil 2025 generated-fixture
acceptance exported, edited, applied and independently reread two COGO points,
including quoted descriptions. Mutating CSV bytes after rehearsal produced a
stale-plan refusal; changing the drawing baseline required a fresh export.
This does not establish live support for an absent Civil year or arbitrary
project data. See [the acceptance report](ACCEPTANCE_20261005.md).
