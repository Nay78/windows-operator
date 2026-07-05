import { describe, expect, it } from "vitest";
import {
  expectedTargetTypeForOperation,
  inferTargetTypeFromShape,
  isShapeCompatibleWithTargetType,
  shapeNameForTargetId,
  targetIdFromShapeName,
} from "../src/template/namedTargetContract";
import { getMockTargetIds, MOCK_TEMPLATE_TARGETS } from "../src/template/targetManifest";

describe("target manifest", () => {
  it("contains stable mock target IDs", () => {
    expect(getMockTargetIds()).toEqual(["TITLE_MAIN", "HERO_IMAGE", "DATA_TABLE"]);
  });

  it("uses unique target IDs and shape names", () => {
    expect(new Set(MOCK_TEMPLATE_TARGETS.map((target) => target.targetId)).size).toBe(MOCK_TEMPLATE_TARGETS.length);
    expect(new Set(MOCK_TEMPLATE_TARGETS.map((target) => target.shapeName)).size).toBe(MOCK_TEMPLATE_TARGETS.length);
  });

  it("maps stable target IDs to PowerPoint shape names", () => {
    expect(shapeNameForTargetId("DATA_TABLE")).toBe("TARGET_DATA_TABLE");
    expect(targetIdFromShapeName("TARGET_DATA_TABLE")).toBe("DATA_TABLE");
    expect(targetIdFromShapeName("Rectangle 7")).toBeUndefined();
    expect(targetIdFromShapeName("TARGET_bad")).toBeUndefined();
  });

  it("derives expected target type from operation kind", () => {
    expect(expectedTargetTypeForOperation({ kind: "replaceText", targetId: "TITLE_MAIN", mode: "plain", text: "x" })).toBe("text");
    expect(expectedTargetTypeForOperation({ kind: "replaceImage", targetId: "HERO_IMAGE" })).toBe("image");
    expect(expectedTargetTypeForOperation({ kind: "readTable", targetId: "DATA_TABLE" })).toBe("table");
  });

  it("infers and validates named target shape compatibility", () => {
    expect(inferTargetTypeFromShape("GeometricShape", undefined, "HERO_IMAGE")).toBe("image");
    expect(inferTargetTypeFromShape("Table", undefined, "DATA_TABLE")).toBe("table");
    expect(isShapeCompatibleWithTargetType("Table", "table")).toBe(true);
    expect(isShapeCompatibleWithTargetType("Table", "text")).toBe(false);
    expect(isShapeCompatibleWithTargetType("GeometricShape", "image")).toBe(true);
  });
});
