import { UpdateFailure } from "../domain/errors";
import type {
  DiscoveredTarget,
  ResolvedArtifact,
  TableRangeUpdateOperation,
  TableSnapshot,
  TargetInspection,
  TargetResult,
  TargetSource,
  TargetType,
  UpdateOperation,
} from "../domain/types";
import type { PresentationAdapter } from "../ports";
import {
  expectedTargetTypeForOperation,
  inferTargetTypeFromShape,
  isShapeCompatibleWithTargetType,
  targetIdFromShapeName,
} from "../template/namedTargetContract";
import { assertPowerPointRequirements, supportsPowerPointApi } from "./requirements";

type NullableOfficeObject<T> = T & { isNullObject: boolean };
type QueuedMutation = () => void;
type TargetTag = "TARGET_ID" | "TARGET_KIND";

interface ShapeTargetRecord {
  targetId: string;
  shape: PowerPoint.Shape;
  shapeId: string;
  shapeName: string;
  shapeType: string;
  targetIdTag?: string;
  targetKindTag?: string;
}

interface BindingTargetRecord extends ShapeTargetRecord {
  binding: PowerPoint.Binding;
}

interface TargetRequest {
  targetId: string;
  expectedType: TargetType;
}

interface BindPlan {
  record: ShapeTargetRecord;
  expectedType: TargetType;
  source: TargetSource;
  addBinding: boolean;
}

export class OfficePresentationAdapter implements PresentationAdapter {
  async discoverTargets(): Promise<DiscoveredTarget[]> {
    assertPowerPointRequirements();

    return PowerPoint.run(async (context) => {
      const bindings = await loadAllBindingTargets(context);
      const namedTargets = await loadNamedShapeTargets(context);
      return mergeDiscoveredTargets(bindings, namedTargets);
    });
  }

  async bindNamedTargets(operations: UpdateOperation[]): Promise<TargetInspection[]> {
    assertPowerPointRequirements();
    validateOperations(operations);
    const requests = targetRequestsForOperations(operations);

    return PowerPoint.run(async (context) => {
      const bindings = await loadBindingTargets(context, requests.map((request) => request.targetId));
      const namedTargets = await loadNamedShapeTargets(context, new Set(requests.map((request) => request.targetId)));
      const inspections: TargetInspection[] = [];
      const plans: BindPlan[] = [];

      for (const request of requests) {
        const binding = bindings.get(request.targetId);
        const namedMatches = namedTargets.filter((record) => record.targetId === request.targetId);
        if (namedMatches.length > 1) {
          throw new UpdateFailure(
            "TARGET_AMBIGUOUS",
            `Named target ${request.targetId} is ambiguous.`,
            `Duplicate shapes named TARGET_${request.targetId}.`,
          );
        }

        const named = namedMatches[0];
        if (binding && named && binding.shapeId !== named.shapeId) {
          throw new UpdateFailure(
            "TARGET_AMBIGUOUS",
            `Named target ${request.targetId} conflicts with an existing binding.`,
            `Binding shape ${binding.shapeId}; named shape ${named.shapeId}.`,
          );
        }

        const record = binding ?? named;
        if (!record) {
          inspections.push({
            targetId: request.targetId,
            found: false,
            editable: false,
            message: `Binding not found and shape TARGET_${request.targetId} was not found.`,
            bound: false,
            tagged: false,
          });
          continue;
        }

        assertCompatibleTarget(record, request.expectedType);
        assertTagsCompatible(record, request.expectedType);
        const source: TargetSource = binding ? "binding" : "repairedName";
        inspections.push({
          ...toInspection(record, request.expectedType, source, true),
          tagged: true,
        });
        plans.push({
          record,
          expectedType: request.expectedType,
          source,
          addBinding: !binding,
        });
      }

      if (inspections.some((inspection) => !inspection.found || !inspection.editable)) {
        return inspections;
      }

      for (const plan of plans) {
        if (plan.addBinding) {
          context.presentation.bindings.add(plan.record.shape, PowerPoint.BindingType.shape, plan.record.targetId);
        }

        queueTagRepair(plan.record.shape, "TARGET_ID", plan.record.targetId, plan.record.targetIdTag);
        queueTagRepair(plan.record.shape, "TARGET_KIND", plan.expectedType, plan.record.targetKindTag);
      }

      if (plans.some((plan) => plan.addBinding || needsTagRepair(plan.record, plan.expectedType))) {
        await context.sync();
      }

      return inspections;
    });
  }

  async inspectTargets(targetIds: string[]): Promise<TargetInspection[]> {
    assertPowerPointRequirements();

    return PowerPoint.run(async (context) => {
      const bindings = await loadBindingTargets(context, targetIds);
      return targetIds.map((targetId) => {
        const binding = bindings.get(targetId);

        if (!binding) {
          return {
            targetId,
            found: false,
            editable: false,
            message: "Binding not found.",
            bound: false,
            tagged: false,
          };
        }

        return toInspection(binding, inferTargetTypeFromShape(binding.shapeType, binding.targetKindTag, targetId), "binding", true);
      });
    });
  }

  async apply(operations: UpdateOperation[], artifacts: Map<string, ResolvedArtifact>): Promise<TargetResult[]> {
    assertPowerPointRequirements();
    validateOperations(operations);

    return PowerPoint.run(async (context) => {
      const targetShapes = operations.map((operation) => ({
        operation,
        shape: context.presentation.bindings.getItem(operation.targetId).getShape(),
      }));

      targetShapes.forEach(({ shape }) => {
        shape.load("id,name,type");
      });

      try {
        await context.sync();
      } catch (error) {
        throw new UpdateFailure(
          "TARGET_NOT_FOUND",
          "One or more required target bindings were not found.",
          String(error),
        );
      }

      const results: TargetResult[] = [];
      const mutations: QueuedMutation[] = [];
      for (const { operation, shape } of targetShapes) {
        try {
          const prepared = await prepareOperation(context, shape, operation, artifacts);
          results.push({ ...prepared.result, shapeName: shape.name, source: "binding", bound: true });
          mutations.push(...prepared.mutations);
        } catch (error) {
          if (error instanceof UpdateFailure) {
            results.push({
              targetId: operation.targetId,
              operationKind: operation.kind,
              status: "failed",
              shapeName: shape.name,
              source: "binding",
              bound: true,
              error: error.updateError,
            });
            continue;
          }
          throw error;
        }
      }

      const failed = results.find((result) => result.status === "failed");
      if (failed) {
        return results;
      }

      mutations.forEach((mutation) => {
        mutation();
      });

      try {
        if (mutations.length > 0) {
          await context.sync();
        }
      } catch (error) {
        throw new UpdateFailure("OFFICE_SYNC_FAILED", "Office failed while applying updates.", String(error), true);
      }

      return results;
    });
  }
}

async function loadAllBindingTargets(context: PowerPoint.RequestContext): Promise<BindingTargetRecord[]> {
  const bindings = context.presentation.bindings;
  bindings.load("items");
  await context.sync();

  const lookups = bindings.items.map((binding) => {
    binding.load("id,type");
    const shape = binding.getShape();
    return createBindingLookup(binding.id, binding, shape);
  });
  await context.sync();

  return lookups.map(toBindingRecord);
}

async function loadBindingTargets(
  context: PowerPoint.RequestContext,
  targetIds: readonly string[],
): Promise<Map<string, BindingTargetRecord>> {
  const uniqueTargetIds = [...new Set(targetIds)];
  const bindingLookups = uniqueTargetIds.map((targetId) => {
    const binding = context.presentation.bindings.getItemOrNullObject(targetId) as NullableOfficeObject<PowerPoint.Binding>;
    binding.load("id,type");
    return { targetId, binding };
  });
  await context.sync();

  const shapeLookups = bindingLookups
    .filter(({ binding }) => !binding.isNullObject)
    .map(({ targetId, binding }) => createBindingLookup(targetId, binding, binding.getShape()));
  await context.sync();

  return new Map(shapeLookups.map((lookup) => [lookup.targetId, toBindingRecord(lookup)]));
}

async function loadNamedShapeTargets(
  context: PowerPoint.RequestContext,
  targetIds?: ReadonlySet<string>,
): Promise<ShapeTargetRecord[]> {
  const slides = context.presentation.slides;
  slides.load("items");
  await context.sync();

  const slideShapes = slides.items.map((slide) => {
    const shapes = slide.shapes;
    shapes.load("items");
    return shapes;
  });
  await context.sync();

  const shapeLookups = slideShapes.flatMap((shapes) =>
    shapes.items.map((shape) => {
      shape.load("id,name,type");
      return shape;
    }),
  );
  await context.sync();

  const targetShapeLookups = shapeLookups
    .map((shape) => ({ shape, targetId: targetIdFromShapeName(shape.name) }))
    .filter((lookup): lookup is { shape: PowerPoint.Shape; targetId: string } => {
      const targetId = lookup.targetId;
      return targetId !== undefined && (!targetIds || targetIds.has(targetId));
    })
    .map(({ shape, targetId }) => createShapeLookup(targetId, shape));
  await context.sync();

  return targetShapeLookups.map(toShapeRecord);
}

function createBindingLookup(targetId: string, binding: PowerPoint.Binding, shape: PowerPoint.Shape) {
  return {
    binding,
    ...createShapeLookup(targetId, shape),
  };
}

function createShapeLookup(targetId: string, shape: PowerPoint.Shape) {
  const targetIdTag = shape.tags.getItemOrNullObject("TARGET_ID") as NullableOfficeObject<PowerPoint.Tag>;
  const targetKindTag = shape.tags.getItemOrNullObject("TARGET_KIND") as NullableOfficeObject<PowerPoint.Tag>;
  shape.load("id,name,type");
  targetIdTag.load("value");
  targetKindTag.load("value");
  return {
    targetId,
    shape,
    targetIdTag,
    targetKindTag,
  };
}

function toBindingRecord(lookup: ReturnType<typeof createBindingLookup>): BindingTargetRecord {
  return {
    binding: lookup.binding,
    ...toShapeRecord(lookup),
  };
}

function toShapeRecord(lookup: ReturnType<typeof createShapeLookup>): ShapeTargetRecord {
  return {
    targetId: lookup.targetId,
    shape: lookup.shape,
    shapeId: lookup.shape.id,
    shapeName: lookup.shape.name,
    shapeType: lookup.shape.type,
    targetIdTag: lookup.targetIdTag.isNullObject ? undefined : lookup.targetIdTag.value,
    targetKindTag: lookup.targetKindTag.isNullObject ? undefined : lookup.targetKindTag.value,
  };
}

function mergeDiscoveredTargets(bindings: BindingTargetRecord[], namedTargets: ShapeTargetRecord[]): DiscoveredTarget[] {
  const byTargetId = new Map<string, DiscoveredTarget & { shapeId?: string }>();
  for (const binding of bindings) {
    const type = inferTargetTypeFromShape(binding.shapeType, binding.targetKindTag, binding.targetId);
    byTargetId.set(binding.targetId, {
      targetId: binding.targetId,
      editable: isShapeCompatibleWithTargetType(binding.shapeType, type),
      type,
      message: "Binding target.",
      shapeName: binding.shapeName,
      source: "binding",
      bound: true,
      tagged: hasExpectedTags(binding, type),
      shapeId: binding.shapeId,
    });
  }

  const namedGroups = groupByTargetId(namedTargets);
  for (const [targetId, records] of namedGroups) {
    const existing = byTargetId.get(targetId);
    if (records.length > 1) {
      const duplicate = {
        targetId,
        editable: false,
        type: inferTargetTypeFromShape(records[0]?.shapeType, records[0]?.targetKindTag, targetId),
        message: `Duplicate named target shapes: TARGET_${targetId}.`,
        shapeName: records[0]?.shapeName,
        source: (existing?.source ?? "name") as TargetSource,
        bound: existing?.bound ?? false,
        tagged: existing?.tagged ?? false,
        shapeId: existing?.shapeId ?? records[0]?.shapeId,
      };
      byTargetId.set(targetId, duplicate);
      continue;
    }

    const record = records[0];
    if (!record) {
      continue;
    }

    const type = inferTargetTypeFromShape(record.shapeType, record.targetKindTag, targetId);
    if (existing) {
      if (existing.shapeId !== record.shapeId) {
        byTargetId.set(targetId, {
          ...existing,
          editable: false,
          message: `Binding target conflicts with named shape TARGET_${targetId}.`,
        });
      }
      continue;
    }

    byTargetId.set(targetId, {
      targetId,
      editable: isShapeCompatibleWithTargetType(record.shapeType, type),
      type,
      message: "Named target.",
      shapeName: record.shapeName,
      source: "name",
      bound: false,
      tagged: hasExpectedTags(record, type),
      shapeId: record.shapeId,
    });
  }

  return [...byTargetId.values()].map(({ shapeId: _shapeId, ...target }) => target);
}

function groupByTargetId(records: ShapeTargetRecord[]): Map<string, ShapeTargetRecord[]> {
  const groups = new Map<string, ShapeTargetRecord[]>();
  for (const record of records) {
    groups.set(record.targetId, [...(groups.get(record.targetId) ?? []), record]);
  }
  return groups;
}

function targetRequestsForOperations(operations: UpdateOperation[]): TargetRequest[] {
  const requests = new Map<string, TargetType>();
  for (const operation of operations) {
    const expectedType = expectedTargetTypeForOperation(operation);
    const existing = requests.get(operation.targetId);
    if (existing && existing !== expectedType) {
      throw new UpdateFailure(
        "TARGET_AMBIGUOUS",
        `Target ${operation.targetId} is used with incompatible operation types.`,
        `expected=${existing}; requested=${expectedType}`,
      );
    }
    requests.set(operation.targetId, expectedType);
  }

  return [...requests.entries()].map(([targetId, expectedType]) => ({ targetId, expectedType }));
}

function assertCompatibleTarget(record: ShapeTargetRecord, expectedType: TargetType): void {
  if (!isShapeCompatibleWithTargetType(record.shapeType, expectedType)) {
    throw new UpdateFailure(
      "TARGET_NOT_EDITABLE",
      `Target ${record.targetId} is not compatible with ${expectedType} updates.`,
      `shapeName=${record.shapeName}; shapeType=${record.shapeType}`,
    );
  }
}

function assertTagsCompatible(record: ShapeTargetRecord, expectedType: TargetType): void {
  if (record.targetIdTag && record.targetIdTag !== record.targetId) {
    throw new UpdateFailure(
      "TARGET_AMBIGUOUS",
      `Target ${record.targetId} has a conflicting TARGET_ID tag.`,
      `shapeName=${record.shapeName}; tag=${record.targetIdTag}`,
    );
  }

  if (record.targetKindTag && record.targetKindTag !== expectedType) {
    throw new UpdateFailure(
      "TARGET_NOT_EDITABLE",
      `Target ${record.targetId} has a conflicting TARGET_KIND tag.`,
      `shapeName=${record.shapeName}; tag=${record.targetKindTag}; expected=${expectedType}`,
    );
  }
}

function toInspection(
  record: ShapeTargetRecord,
  expectedType: TargetType,
  source: TargetSource,
  bound: boolean,
): TargetInspection {
  return {
    targetId: record.targetId,
    found: true,
    editable: isShapeCompatibleWithTargetType(record.shapeType, expectedType),
    type: expectedType,
    shapeName: record.shapeName,
    source,
    bound,
    tagged: hasExpectedTags(record, expectedType),
  };
}

function hasExpectedTags(record: ShapeTargetRecord, expectedType: TargetType): boolean {
  return record.targetIdTag === record.targetId && record.targetKindTag === expectedType;
}

function needsTagRepair(record: ShapeTargetRecord, expectedType: TargetType): boolean {
  return record.targetIdTag !== record.targetId || record.targetKindTag !== expectedType;
}

function queueTagRepair(shape: PowerPoint.Shape, tag: TargetTag, value: string, existing?: string): void {
  if (existing === value) {
    return;
  }

  shape.tags.add(tag, value);
}

function validateOperations(operations: UpdateOperation[]): void {
  for (const operation of operations) {
    if (operation.kind === "replaceText") {
      if (typeof operation.text !== "string") {
        throw new UpdateFailure("UPDATE_FAILED", `Text target ${operation.targetId} is missing text.`);
      }

      if (!operation.allowEmpty && operation.text.trim() === "") {
        throw new UpdateFailure("TARGET_NOT_EDITABLE", `Text target ${operation.targetId} cannot be empty.`);
      }
      continue;
    }

    if (operation.kind === "replaceImage") {
      if (!operation.artifact) {
        throw new UpdateFailure("ARTIFACT_NOT_FOUND", `Image target ${operation.targetId} is missing artifact.`);
      }
      continue;
    }

    if (operation.kind === "readTable") {
      continue;
    }

    if (operation.kind === "replaceTableCell") {
      validateTableIndex(operation.rowIndex, "rowIndex", operation.targetId);
      validateTableIndex(operation.columnIndex, "columnIndex", operation.targetId);
      if (typeof operation.text !== "string") {
        throw new UpdateFailure("UPDATE_FAILED", `Table target ${operation.targetId} is missing cell text.`);
      }

      if (!operation.allowEmpty && operation.text.trim() === "") {
        throw new UpdateFailure("TARGET_NOT_EDITABLE", `Table target ${operation.targetId} cell text cannot be empty.`);
      }
      continue;
    }

    if (!operation.values) {
      throw new UpdateFailure("UPDATE_FAILED", `Table target ${operation.targetId} is missing range values.`);
    }

    validateTableIndex(operation.startRowIndex ?? 0, "startRowIndex", operation.targetId);
    validateTableIndex(operation.startColumnIndex ?? 0, "startColumnIndex", operation.targetId);
    validateTableValues(operation, operation.values);
  }
}

function validateTableIndex(value: number | undefined, name: string, targetId: string): void {
  if (value === undefined || !Number.isInteger(value) || value < 0) {
    throw new UpdateFailure("UPDATE_FAILED", `Table target ${targetId} requires non-negative integer ${name}.`);
  }
}

function validateTableValues(operation: TableRangeUpdateOperation, values: string[][]): void {
  if (!Array.isArray(values) || values.length === 0) {
    throw new UpdateFailure("UPDATE_FAILED", `Table target ${operation.targetId} requires at least one value row.`);
  }

  const width = values[0]?.length ?? 0;
  if (width === 0) {
    throw new UpdateFailure("UPDATE_FAILED", `Table target ${operation.targetId} requires at least one value column.`);
  }

  for (const row of values) {
    if (!Array.isArray(row) || row.length !== width) {
      throw new UpdateFailure("UPDATE_FAILED", `Table target ${operation.targetId} range values must be rectangular.`);
    }

    for (const value of row) {
      if (typeof value !== "string") {
        throw new UpdateFailure("UPDATE_FAILED", `Table target ${operation.targetId} range values must be strings.`);
      }

      if (!operation.allowEmpty && value.trim() === "") {
        throw new UpdateFailure("TARGET_NOT_EDITABLE", `Table target ${operation.targetId} range values cannot be empty.`);
      }
    }
  }
}

async function prepareOperation(
  context: PowerPoint.RequestContext,
  shape: PowerPoint.Shape,
  operation: UpdateOperation,
  artifacts: Map<string, ResolvedArtifact>,
): Promise<{ result: TargetResult; mutations: QueuedMutation[] }> {
  if (operation.kind === "readTable") {
    const table = await loadTableSnapshot(context, shape, operation.targetId);
    return {
      result: {
        targetId: operation.targetId,
        operationKind: operation.kind,
        status: "succeeded",
        type: "table",
        table,
      },
      mutations: [],
    };
  }

  if (operation.kind === "replaceTableCell") {
    const table = await loadTableForMutation(context, shape, operation.targetId);
    const rowIndex = operation.rowIndex ?? 0;
    const columnIndex = operation.columnIndex ?? 0;
    validateCellInBounds(table, operation.targetId, rowIndex, columnIndex);
    const cell = table.getCellOrNullObject(rowIndex, columnIndex) as NullableOfficeObject<PowerPoint.TableCell>;
    cell.load("text");
    await context.sync();
    if (cell.isNullObject) {
      throw new UpdateFailure("TARGET_NOT_EDITABLE", `Table target ${operation.targetId} cell is not editable.`);
    }

    return {
      result: {
        targetId: operation.targetId,
        operationKind: operation.kind,
        status: "succeeded",
        type: "table",
      },
      mutations: [() => {
        cell.text = normalizeCellText(operation.text ?? "");
      }],
    };
  }

  if (operation.kind === "replaceTableRange") {
    const values = operation.values ?? [];
    const table = await loadTableForMutation(context, shape, operation.targetId);
    const startRowIndex = operation.startRowIndex ?? 0;
    const startColumnIndex = operation.startColumnIndex ?? 0;
    validateRangeInBounds(table, operation.targetId, startRowIndex, startColumnIndex, values);
    const cellLookups = values.flatMap((row, rowOffset) =>
      row.map((_value, columnOffset) => ({
        rowOffset,
        columnOffset,
        cell: table.getCellOrNullObject(startRowIndex + rowOffset, startColumnIndex + columnOffset) as NullableOfficeObject<PowerPoint.TableCell>,
      })),
    );
    cellLookups.forEach(({ cell }) => {
      cell.load("text");
    });
    await context.sync();
    const blocked = cellLookups.find(({ cell }) => cell.isNullObject);
    if (blocked) {
      throw new UpdateFailure(
        "TARGET_NOT_EDITABLE",
        `Table target ${operation.targetId} contains a non-editable merged cell continuation.`,
      );
    }

    return {
      result: {
        targetId: operation.targetId,
        operationKind: operation.kind,
        status: "succeeded",
        type: "table",
      },
      mutations: cellLookups.map(({ rowOffset, columnOffset, cell }) => () => {
        cell.text = normalizeCellText(values[rowOffset][columnOffset]);
      }),
    };
  }

  return {
    result: {
      targetId: operation.targetId,
      operationKind: operation.kind,
      status: "succeeded",
      type: operation.kind === "replaceText" ? "text" : "image",
    },
    mutations: [() => {
      queueShapeOperation(shape, operation, artifacts);
    }],
  };
}

async function loadTableSnapshot(
  context: PowerPoint.RequestContext,
  shape: PowerPoint.Shape,
  targetId: string,
): Promise<TableSnapshot> {
  const table = await loadTableForMutation(context, shape, targetId);
  return {
    rowCount: table.rowCount,
    columnCount: table.columnCount,
    values: table.values.map((row) => row.map((value) => value ?? "")),
  };
}

async function loadTableForMutation(
  context: PowerPoint.RequestContext,
  shape: PowerPoint.Shape,
  targetId: string,
): Promise<PowerPoint.Table> {
  if (shape.type !== "Table") {
    throw new UpdateFailure("TARGET_NOT_EDITABLE", `Target ${targetId} is not a table.`);
  }

  const table = shape.getTable();
  table.load("rowCount,columnCount,values");
  await context.sync();
  return table;
}

function validateCellInBounds(table: PowerPoint.Table, targetId: string, rowIndex: number, columnIndex: number): void {
  if (rowIndex >= table.rowCount || columnIndex >= table.columnCount) {
    throw new UpdateFailure(
      "TARGET_NOT_EDITABLE",
      `Table target ${targetId} cell is outside bounds ${table.rowCount}x${table.columnCount}.`,
    );
  }
}

function validateRangeInBounds(
  table: PowerPoint.Table,
  targetId: string,
  startRowIndex: number,
  startColumnIndex: number,
  values: string[][],
): void {
  const rowCount = values.length;
  const columnCount = values[0]?.length ?? 0;
  if (startRowIndex + rowCount > table.rowCount || startColumnIndex + columnCount > table.columnCount) {
    throw new UpdateFailure(
      "TARGET_NOT_EDITABLE",
      `Table target ${targetId} range is outside bounds ${table.rowCount}x${table.columnCount}.`,
    );
  }
}

function normalizeCellText(value: string): string {
  return value.replace(/\r\n?/gu, "\n");
}

function queueShapeOperation(
  shape: PowerPoint.Shape,
  operation: Extract<UpdateOperation, { kind: "replaceText" | "replaceImage" }>,
  artifacts: Map<string, ResolvedArtifact>,
): void {
  if (operation.kind === "replaceText") {
    const text = operation.text;
    if (typeof text !== "string") {
      throw new UpdateFailure("UPDATE_FAILED", `Text target ${operation.targetId} is missing text.`);
    }

    shape.textFrame.textRange.text = text.replace(/\r\n?/gu, "\n");
    return;
  }

  const artifactRef = operation.artifact;
  if (!artifactRef) {
      throw new UpdateFailure("ARTIFACT_NOT_FOUND", `Image target ${operation.targetId} is missing artifact.`);
    }

  const artifact = artifacts.get(artifactRef.artifactId);
  if (!artifact) {
    throw new UpdateFailure("ARTIFACT_NOT_FOUND", `Artifact ${artifactRef.artifactId} was not resolved.`);
  }

  shape.fill.setImage(artifact.base64);

  if (operation.altText && supportsPowerPointApi("1.10")) {
    shape.altTextDescription = operation.altText;
  }
}
