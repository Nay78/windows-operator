import { describe, expect, it } from "vitest";
import { UpdateFailure } from "../src/domain/errors";
import { buildTableGeometry, findUniqueTableColumn, normalizeTableLookupText } from "../src/office/tableGeometry";

describe("table geometry primitives", () => {
  it("computes absolute column and row bounds from table origin and sizes", () => {
    const geometry = buildTableGeometry(
      { left: 100, top: 20, width: 300, height: 60 },
      [80, 120, 100],
      [25, 35],
    );

    expect(geometry.columns).toEqual([
      { columnIndex: 0, left: 100, width: 80, right: 180 },
      { columnIndex: 1, left: 180, width: 120, right: 300 },
      { columnIndex: 2, left: 300, width: 100, right: 400 },
    ]);
    expect(geometry.rows).toEqual([
      { rowIndex: 0, top: 20, height: 25, bottom: 45 },
      { rowIndex: 1, top: 45, height: 35, bottom: 80 },
    ]);
  });

  it("normalizes date lookup text deterministically", () => {
    expect(normalizeTableLookupText("  08-JUL\n")).toBe("08-jul");
    expect(normalizeTableLookupText("08   jul")).toBe("08 jul");
  });

  it("finds exactly one matching table column", () => {
    const match = findUniqueTableColumn(
      [["07-jul", " 08-JUL ", "09-jul"]],
      0,
      "08-jul",
      "DATA_TABLE",
    );

    expect(match).toEqual({ rowIndex: 0, columnIndex: 1, text: " 08-JUL " });
  });

  it("fails when date lookup has zero matches", () => {
    expect(() => findUniqueTableColumn([["07-jul"]], 0, "08-jul", "DATA_TABLE")).toThrow(UpdateFailure);
  });

  it("fails when date lookup has duplicate matches", () => {
    expect(() => findUniqueTableColumn([["08-jul", "08-JUL"]], 0, "08-jul", "DATA_TABLE")).toThrow(UpdateFailure);
  });
});
