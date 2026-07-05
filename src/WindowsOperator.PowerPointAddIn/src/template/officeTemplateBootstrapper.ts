import { UpdateFailure } from "../domain/errors";
import { assertPowerPointRequirements, supportsPowerPointApi } from "../office/requirements";
import { MOCK_TEMPLATE_TARGETS, type TemplateTarget } from "./targetManifest";

type NullableOfficeObject<T> = T & { isNullObject: boolean };

export interface TemplateBootstrapResult {
  created: string[];
  existing: string[];
}

export interface TemplateCleanupResult {
  deleted: string[];
  missing: string[];
  skipped: string[];
}

export class OfficeTemplateBootstrapper {
  async ensureMockTargets(): Promise<TemplateBootstrapResult> {
    assertPowerPointRequirements();

    return PowerPoint.run(async (context) => {
      const slide = await getTargetSlide(context);
      const bindings = MOCK_TEMPLATE_TARGETS.map((target) => ({
        target,
        binding: context.presentation.bindings.getItemOrNullObject(target.targetId) as NullableOfficeObject<PowerPoint.Binding>,
      }));

      bindings.forEach(({ binding }) => {
        binding.load("id,type");
      });
      await context.sync();

      const result: TemplateBootstrapResult = {
        created: [],
        existing: [],
      };

      for (const { target, binding } of bindings) {
        if (!binding.isNullObject) {
          result.existing.push(target.targetId);
          continue;
        }

        const shape = createTargetShape(slide, target);
        context.presentation.bindings.add(shape, PowerPoint.BindingType.shape, target.targetId);
        result.created.push(target.targetId);
      }

      await context.sync();
      return result;
    });
  }

  async ensureNamedOnlyMockTargets(): Promise<TemplateBootstrapResult> {
    assertPowerPointRequirements();

    return PowerPoint.run(async (context) => {
      const slide = await getTargetSlide(context);
      const shapes = await loadPresentationShapes(context);
      const result: TemplateBootstrapResult = {
        created: [],
        existing: [],
      };

      for (const target of MOCK_TEMPLATE_TARGETS) {
        if (shapes.find((shape) => shape.name === target.shapeName)) {
          result.existing.push(target.targetId);
          continue;
        }

        const shape = createShapeForTarget(slide, target);
        shape.name = target.shapeName;
        result.created.push(target.targetId);
      }

      await context.sync();
      return result;
    });
  }

  async cleanupMockTargets(): Promise<TemplateCleanupResult> {
    assertPowerPointRequirements();

    return PowerPoint.run(async (context) => {
      const bindingLookups = MOCK_TEMPLATE_TARGETS.map((target) => ({
        target,
        binding: context.presentation.bindings.getItemOrNullObject(target.targetId) as NullableOfficeObject<PowerPoint.Binding>,
        shape: null as PowerPoint.Shape | null,
        targetIdTag: null as NullableOfficeObject<PowerPoint.Tag> | null,
      }));

      bindingLookups.forEach(({ binding }) => {
        binding.load("id,type");
      });
      await context.sync();

      const result: TemplateCleanupResult = {
        deleted: [],
        missing: [],
        skipped: [],
      };

      for (const lookup of bindingLookups) {
        if (lookup.binding.isNullObject) {
          result.missing.push(lookup.target.targetId);
          continue;
        }

        lookup.shape = lookup.binding.getShape();
        lookup.shape.load("id,name");
        lookup.targetIdTag = lookup.shape.tags.getItemOrNullObject("TARGET_ID") as NullableOfficeObject<PowerPoint.Tag>;
        lookup.targetIdTag.load("value");
      }

      try {
        await context.sync();
      } catch (error) {
        throw new UpdateFailure(
          "OFFICE_SYNC_FAILED",
          "Office failed while inspecting template targets for cleanup.",
          String(error),
          true,
        );
      }

      for (const { target, binding, shape, targetIdTag } of bindingLookups) {
        if (binding.isNullObject) {
          continue;
        }

        if (!shape || !targetIdTag || targetIdTag.isNullObject || targetIdTag.value !== target.targetId) {
          result.skipped.push(target.targetId);
          continue;
        }

        binding.delete();
        shape.delete();
        result.deleted.push(target.targetId);
      }

      await context.sync();
      return result;
    });
  }

  async cleanupNamedOnlyMockTargets(): Promise<TemplateCleanupResult> {
    assertPowerPointRequirements();

    return PowerPoint.run(async (context) => {
      const targetIds = new Set(MOCK_TEMPLATE_TARGETS.map((target) => target.targetId));
      const targetNames = new Set(MOCK_TEMPLATE_TARGETS.map((target) => target.shapeName));
      const bindings = await loadBindingLookupsForTargets(context, [...targetIds]);
      const shapes = await loadPresentationShapes(context);
      const result: TemplateCleanupResult = {
        deleted: [],
        missing: [],
        skipped: [],
      };

      for (const target of MOCK_TEMPLATE_TARGETS) {
        const matches = shapes.filter((shape) => shape.name === target.shapeName);
        if (matches.length === 0) {
          result.missing.push(target.targetId);
          continue;
        }

        for (const shape of matches) {
          const binding = bindings.find((lookup) => lookup.targetId === target.targetId && lookup.shapeId === shape.id);
          binding?.binding.delete();
          shape.delete();
          result.deleted.push(target.targetId);
        }
      }

      const skippedBindings = bindings.filter((lookup) => targetIds.has(lookup.targetId) && !targetNames.has(lookup.shapeName));
      result.skipped.push(...skippedBindings.map((lookup) => lookup.targetId));

      await context.sync();
      return result;
    });
  }
}

async function getTargetSlide(context: PowerPoint.RequestContext): Promise<PowerPoint.Slide> {
  const selectedSlides = context.presentation.getSelectedSlides();
  const selectedCount = selectedSlides.getCount();
  const slideCount = context.presentation.slides.getCount();
  await context.sync();

  if (selectedCount.value > 0) {
    return selectedSlides.getItemAt(0);
  }

  if (slideCount.value > 0) {
    return context.presentation.slides.getItemAt(0);
  }

  throw new UpdateFailure("TARGET_NOT_FOUND", "Presentation has no slide to prepare.");
}

function createTargetShape(slide: PowerPoint.Slide, target: TemplateTarget): PowerPoint.Shape {
  const shape = createShapeForTarget(slide, target);

  shape.name = target.shapeName;
  shape.tags.add("TARGET_ID", target.targetId);
  shape.tags.add("TARGET_KIND", target.kind);

  if (target.kind === "text") {
    shape.textFrame.textRange.font.size = 32;
    shape.textFrame.textRange.font.color = "#1f2328";
    return shape;
  }

  if (target.kind === "table") {
    return shape;
  }

  shape.fill.setSolidColor("#dbeafe");
  shape.textFrame.textRange.text = "HERO_IMAGE";
  shape.textFrame.textRange.font.size = 24;
  shape.textFrame.textRange.font.color = "#0969da";

  if (supportsPowerPointApi("1.10")) {
    shape.altTextDescription = "Hero image placeholder.";
  }

  return shape;
}

function createShapeForTarget(slide: PowerPoint.Slide, target: TemplateTarget): PowerPoint.Shape {
  if (target.kind === "text") {
    return slide.shapes.addTextBox("Template title placeholder", {
      left: 48,
      top: 48,
      width: 560,
      height: 64,
    });
  }

  if (target.kind === "table") {
    return slide.shapes.addTable(3, 3, {
      left: 48,
      top: 440,
      width: 560,
      height: 120,
      values: [
        ["Metric", "Plan", "Actual"],
        ["Tonnes", "0", "0"],
        ["Availability", "0%", "0%"],
      ],
    });
  }

  return slide.shapes.addGeometricShape(PowerPoint.GeometricShapeType.rectangle, {
      left: 48,
      top: 140,
      width: 560,
      height: 280,
    });
}

async function loadPresentationShapes(context: PowerPoint.RequestContext): Promise<PowerPoint.Shape[]> {
  const slides = context.presentation.slides;
  slides.load("items");
  await context.sync();

  const shapeCollections = slides.items.map((slide) => {
    const shapes = slide.shapes;
    shapes.load("items");
    return shapes;
  });
  await context.sync();

  const shapes = shapeCollections.flatMap((collection) => collection.items);
  shapes.forEach((shape) => {
    shape.load("id,name,type");
  });
  await context.sync();
  return shapes;
}

async function loadBindingLookupsForTargets(
  context: PowerPoint.RequestContext,
  targetIds: string[],
): Promise<Array<{ targetId: string; binding: PowerPoint.Binding; shapeId: string; shapeName: string }>> {
  const bindingLookups = targetIds.map((targetId) => {
    const binding = context.presentation.bindings.getItemOrNullObject(targetId) as NullableOfficeObject<PowerPoint.Binding>;
    binding.load("id,type");
    return { targetId, binding };
  });
  await context.sync();

  const shapeLookups = bindingLookups
    .filter(({ binding }) => !binding.isNullObject)
    .map(({ targetId, binding }) => {
      const shape = binding.getShape();
      shape.load("id,name");
      return { targetId, binding, shape };
    });
  await context.sync();

  return shapeLookups.map(({ targetId, binding, shape }) => ({
    targetId,
    binding,
    shapeId: shape.id,
    shapeName: shape.name,
  }));
}
