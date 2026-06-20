import { UpdateFailure } from "../domain/errors";
import type { ResolvedArtifact, TargetInspection, TargetResult, UpdateOperation } from "../domain/types";
import type { PresentationAdapter } from "../ports";
import { assertPowerPointRequirements, supportsPowerPointApi } from "./requirements";

type NullableOfficeObject<T> = T & { isNullObject: boolean };

export class OfficePresentationAdapter implements PresentationAdapter {
  async inspectTargets(targetIds: string[]): Promise<TargetInspection[]> {
    assertPowerPointRequirements();

    return PowerPoint.run(async (context) => {
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
          shape.load("id,name,type");
          return { targetId, shape };
        });

      await context.sync();

      return targetIds.map((targetId) => {
        const binding = bindingLookups.find((lookup) => lookup.targetId === targetId)?.binding;
        const shape = shapeLookups.find((lookup) => lookup.targetId === targetId)?.shape;

        if (!binding || binding.isNullObject || !shape) {
          return {
            targetId,
            found: false,
            editable: false,
            message: "Binding not found.",
          };
        }

        return {
          targetId,
          found: true,
          editable: true,
          type: "unknown",
        };
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
      for (const { operation, shape } of targetShapes) {
        try {
          queueOperation(shape, operation, artifacts);
          results.push({
            targetId: operation.targetId,
            operationKind: operation.kind,
            status: "succeeded",
          });
        } catch (error) {
          if (error instanceof UpdateFailure) {
            results.push({
              targetId: operation.targetId,
              operationKind: operation.kind,
              status: "failed",
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

      try {
        await context.sync();
      } catch (error) {
        throw new UpdateFailure("OFFICE_SYNC_FAILED", "Office failed while applying updates.", String(error), true);
      }

      return results;
    });
  }
}

function validateOperations(operations: UpdateOperation[]): void {
  for (const operation of operations) {
    if (operation.kind === "replaceText" && !operation.allowEmpty && operation.text.trim() === "") {
      throw new UpdateFailure("TARGET_NOT_EDITABLE", `Text target ${operation.targetId} cannot be empty.`);
    }
  }
}

function queueOperation(
  shape: PowerPoint.Shape,
  operation: UpdateOperation,
  artifacts: Map<string, ResolvedArtifact>,
): void {
  if (operation.kind === "replaceText") {
    shape.textFrame.textRange.text = operation.text.replace(/\r\n?/gu, "\n");
    return;
  }

  const artifact = artifacts.get(operation.artifact.artifactId);
  if (!artifact) {
    throw new UpdateFailure("ARTIFACT_NOT_FOUND", `Artifact ${operation.artifact.artifactId} was not resolved.`);
  }

  shape.fill.setImage(artifact.base64);

  if (operation.altText && supportsPowerPointApi("1.10")) {
    shape.altTextDescription = operation.altText;
  }
}
