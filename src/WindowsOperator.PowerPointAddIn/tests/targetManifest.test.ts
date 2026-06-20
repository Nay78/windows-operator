import { describe, expect, it } from "vitest";
import { getMockTargetIds, MOCK_TEMPLATE_TARGETS } from "../src/template/targetManifest";

describe("target manifest", () => {
  it("contains stable mock target IDs", () => {
    expect(getMockTargetIds()).toEqual(["TITLE_MAIN", "HERO_IMAGE"]);
  });

  it("uses unique target IDs and shape names", () => {
    expect(new Set(MOCK_TEMPLATE_TARGETS.map((target) => target.targetId)).size).toBe(MOCK_TEMPLATE_TARGETS.length);
    expect(new Set(MOCK_TEMPLATE_TARGETS.map((target) => target.shapeName)).size).toBe(MOCK_TEMPLATE_TARGETS.length);
  });
});
