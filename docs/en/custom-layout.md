# Custom Layout (Dashboard)

This document explains how to customize the Dashboard layout via `mfa_layout.json`.
It also describes the read/write rules and a small tip for resource developers.

---

## Location

MFA searches the `resource` folder and **recursively** loads the first `mfa_layout.json`.

Recommended locations:
- `resource/mfa_layout.json`
- or `resource/base/mfa_layout.json`

Write rule:
- When MFA generates a layout file, it always writes to `resource/mfa_layout.json`.

---

## File Structure

`mfa_layout.json` contains:

- Global layout settings: `rows`, `columns`, `spacing`
- Layout for each card: key is the card ID

Example:

```json
{
  "rows": 3,
  "columns": 4,
  "spacing": 10,
  "TaskList": {
    "row": 0,
    "col": 0,
    "row_span": 2,
    "col_span": 2
  },
  "LogPanel": {
    "row": 0,
    "col": 2,
    "row_span": 3,
    "col_span": 2
  }
}
```

---

## Default Layout Example

Below is the current default layout (TaskQueueView) as a reference:

```json
{
  "rows": 8,
  "columns": 12,
  "spacing": 10,
  "settings": {
    "row": 0,
    "col": 4,
    "row_span": 5,
    "col_span": 4
  },
  "task_list": {
    "row": 0,
    "col": 0,
    "row_span": 8,
    "col_span": 4
  },
  "task_desc": {
    "row": 5,
    "col": 4,
    "row_span": 3,
    "col_span": 4
  },
  "live_view": {
    "row": 0,
    "col": 8,
    "row_span": 4,
    "col_span": 4
  },
  "log": {
    "row": 4,
    "col": 8,
    "row_span": 4,
    "col_span": 4
  }
}
```
---

## Fields

### Global fields

- `rows`: row count
- `columns`: column count
- `spacing`: cell spacing

### Card fields

Each card entry is an object with:

- `row`: start row (0-based)
- `col`: start column (0-based)
- `row_span` / `rowSpan`: row span
- `col_span` / `colSpan`: column span

---

## Notes

- Unknown card IDs are ignored
- Unspecified cards keep the default layout
- Drag/resize in the UI saves to local config, but does not overwrite `mfa_layout.json`
- Only when `rows` or `columns` differs from the current config will MFA force-refresh config layout from `mfa_layout.json`
- If `mfa_layout.json` content changes (hash update), MFA force-applies the resource layout and overwrites current layout

---

## Tip for Resource Developers

To quickly generate a new layout file:

1. Adjust `rows` / `columns` in `mfa_layout.json`
2. Open the UI and drag/resize to the desired layout
3. Delete `mfa_layout.json`
4. Reopen the UI; it will auto-generate a new layout file