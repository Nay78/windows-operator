import { UpdateFailure } from "../domain/errors";
import type { ShapeBounds, TableGeometry, TableMatch } from "../domain/types";

export function buildTableGeometry(
  bounds: ShapeBounds,
  columnWidths: number[],
  rowHeights: number[],
): TableGeometry {
  let left = bounds.left;
  const columns = columnWidths.map((width, columnIndex) => {
    const column = {
      columnIndex,
      left,
      width,
      right: left + width,
    };
    left = column.right;
    return column;
  });

  let top = bounds.top;
  const rows = rowHeights.map((height, rowIndex) => {
    const row = {
      rowIndex,
      top,
      height,
      bottom: top + height,
    };
    top = row.bottom;
    return row;
  });

  return {
    bounds,
    columns,
    rows,
  };
}

export function findUniqueTableColumn(
  values: string[][],
  rowIndex: number,
  text: string,
  targetId: string,
): TableMatch {
  if (!Number.isInteger(rowIndex) || rowIndex < 0 || rowIndex >= values.length) {
    throw new UpdateFailure("TARGET_NOT_EDITABLE", `Table target ${targetId} row is outside bounds.`);
  }

  const expected = normalizeTableLookupText(text);
  if (!expected) {
    throw new UpdateFailure("UPDATE_FAILED", `Table target ${targetId} requires non-empty search text.`);
  }

  const row = values[rowIndex] ?? [];
  const matches = row
    .map((value, columnIndex) => ({ value, columnIndex }))
    .filter(({ value }) => normalizeTableLookupText(value) === expected);

  if (matches.length === 0) {
    throw new UpdateFailure("TARGET_NOT_FOUND", `Table target ${targetId} has no matching column.`);
  }

  if (matches.length > 1) {
    throw new UpdateFailure("TARGET_AMBIGUOUS", `Table target ${targetId} has multiple matching columns.`);
  }

  const match = matches[0];
  return {
    rowIndex,
    columnIndex: match.columnIndex,
    text: match.value,
  };
}

export function normalizeTableLookupText(value: string): string {
  return value.trim().replace(/\s+/gu, " ").toLowerCase();
}
