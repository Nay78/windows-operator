import { UpdateFailure } from "../domain/errors";
import { assertPowerPointRequirements, supportsPowerPointApi } from "../office/requirements";
import { MOCK_TEMPLATE_TARGETS, type TemplateTarget } from "./targetManifest";

type NullableOfficeObject<T> = T & { isNullObject: boolean };

export interface TemplateBootstrapResult {
  created: string[];
  existing: string[];
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
  const shape = target.kind === "text"
    ? slide.shapes.addTextBox("Template title placeholder", {
      left: 48,
      top: 48,
      width: 560,
      height: 64,
    })
    : slide.shapes.addGeometricShape(PowerPoint.GeometricShapeType.rectangle, {
      left: 48,
      top: 140,
      width: 560,
      height: 280,
    });

  shape.name = target.shapeName;
  shape.tags.add("TARGET_ID", target.targetId);
  shape.tags.add("TARGET_KIND", target.kind);

  if (target.kind === "text") {
    shape.textFrame.textRange.font.size = 32;
    shape.textFrame.textRange.font.color = "#1f2328";
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
