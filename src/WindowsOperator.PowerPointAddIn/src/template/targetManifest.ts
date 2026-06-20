export type TemplateTargetKind = "text" | "image";

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
    shapeName: "TARGET_TITLE_MAIN",
    description: "Main title text box.",
  },
  {
    targetId: "HERO_IMAGE",
    kind: "image",
    shapeName: "TARGET_HERO_IMAGE",
    description: "Hero image placeholder.",
  },
];

export function getMockTargetIds(): string[] {
  return MOCK_TEMPLATE_TARGETS.map((target) => target.targetId);
}
