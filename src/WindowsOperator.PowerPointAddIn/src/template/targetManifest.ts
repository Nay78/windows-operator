import { shapeNameForTargetId } from "./namedTargetContract";

export type TemplateTargetKind = "text" | "image" | "table";

export interface TemplateTarget {
  targetId: string;
  kind: TemplateTargetKind;
  shapeName: string;
  description: string;
}

export const MOCK_TEMPLATE_TARGETS: TemplateTarget[] = [
  {
    targetId: "TITLE_MAIN",
    kind: "text",
    shapeName: shapeNameForTargetId("TITLE_MAIN"),
    description: "Main title text box.",
  },
  {
    targetId: "HERO_IMAGE",
    kind: "image",
    shapeName: shapeNameForTargetId("HERO_IMAGE"),
    description: "Hero image placeholder.",
  },
  {
    targetId: "DATA_TABLE",
    kind: "table",
    shapeName: shapeNameForTargetId("DATA_TABLE"),
    description: "Structured table target.",
  },
];

export function getMockTargetIds(): string[] {
  return MOCK_TEMPLATE_TARGETS.map((target) => target.targetId);
}
