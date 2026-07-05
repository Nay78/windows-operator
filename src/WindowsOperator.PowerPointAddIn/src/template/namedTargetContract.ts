import type { TargetType, UpdateOperation } from "../domain/types";

export const TARGET_NAME_PREFIX = "TARGET_";
const TARGET_ID_PATTERN = /^[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)*$/u;

export function shapeNameForTargetId(targetId: string): string {
  return `${TARGET_NAME_PREFIX}${targetId}`;
}

export function targetIdFromShapeName(shapeName?: string): string | undefined {
  const name = shapeName?.trim();
  if (!name?.startsWith(TARGET_NAME_PREFIX)) {
    return undefined;
  }

  const targetId = name.slice(TARGET_NAME_PREFIX.length);
  return TARGET_ID_PATTERN.test(targetId) ? targetId : undefined;
}

export function expectedTargetTypeForOperation(operation: UpdateOperation): TargetType {
  if (operation.kind === "replaceText") {
    return "text";
  }

  if (operation.kind === "replaceImage") {
    return "image";
  }

  return "table";
}

export function inferTargetTypeFromName(targetId: string): TargetType {
  if (/(^|_)TABLE(_|$)/u.test(targetId)) {
    return "table";
  }

  if (/(^|_)(IMAGE|IMG|PICTURE|PHOTO)(_|$)/u.test(targetId)) {
    return "image";
  }

  if (/(^|_)(TEXT|TITLE|LABEL|VALUE)(_|$)/u.test(targetId)) {
    return "text";
  }

  return "unknown";
}

export function inferTargetTypeFromShape(shapeType?: string, taggedKind?: string, targetId?: string): TargetType {
  if (taggedKind === "text" || taggedKind === "image" || taggedKind === "table") {
    return taggedKind;
  }

  if (shapeType === "Table") {
    return "table";
  }

  if (shapeType === "Image") {
    return "image";
  }

  if (shapeType === "TextBox") {
    return "text";
  }

  return targetId ? inferTargetTypeFromName(targetId) : "unknown";
}

export function isShapeCompatibleWithTargetType(shapeType: string | undefined, expectedType: TargetType): boolean {
  if (expectedType === "unknown") {
    return true;
  }

  if (expectedType === "table") {
    return shapeType === "Table";
  }

  if (expectedType === "text") {
    return shapeType === "TextBox" || shapeType === "GeometricShape" || shapeType === "Placeholder" || shapeType === "Callout";
  }

  return shapeType !== "Table" && shapeType !== "Unsupported";
}
