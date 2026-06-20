import { toUpdateError, UpdateFailure } from "../domain/errors";
import type { ResolvedArtifact, TargetResult, UpdateErrorCode, UpdateJob, UpdateResult } from "../domain/types";
import type { ArtifactResolver, CurrentDocumentProvider, PresentationAdapter } from "../ports";

export class UpdateEngine {
  constructor(
    private readonly presentation: PresentationAdapter,
    private readonly artifacts: ArtifactResolver,
    private readonly currentDocument: CurrentDocumentProvider,
  ) {}

  async apply(job: UpdateJob): Promise<UpdateResult> {
    const startedAt = new Date().toISOString();
    this.assertDocumentMatch(job);

    const artifactMap = await this.resolveArtifacts(job);
    await this.prevalidateTargets(job);
    const targets = await this.presentation.apply(job.operations, artifactMap);

    const failedTarget = targets.find((target) => target.status === "failed");

    return {
      jobId: job.jobId,
      status: failedTarget ? "failed" : "succeeded",
      startedAt,
      finishedAt: new Date().toISOString(),
      targets,
    };
  }

  private assertDocumentMatch(job: UpdateJob): void {
    if (!job.expectedDocumentUrl) {
      return;
    }

    const actual = normalizeUrl(this.currentDocument.getUrl());
    const expected = normalizeUrl(job.expectedDocumentUrl);
    if (actual !== expected) {
      throw new UpdateFailure(
        "FILE_NOT_EDITABLE",
        "Active presentation does not match the queued job.",
        `expected=${job.expectedDocumentUrl}; actual=${this.currentDocument.getUrl() ?? ""}`,
      );
    }
  }

  private async resolveArtifacts(job: UpdateJob): Promise<Map<string, ResolvedArtifact>> {
    const resolved = new Map<string, ResolvedArtifact>();
    for (const operation of job.operations) {
      if (operation.kind !== "replaceImage") {
        continue;
      }
      if (resolved.has(operation.artifact.artifactId)) {
        continue;
      }
      const artifact = await this.artifacts.resolve(operation.artifact);
      resolved.set(artifact.artifactId, artifact);
    }
    return resolved;
  }

  private async prevalidateTargets(job: UpdateJob): Promise<void> {
    const inspections = await this.presentation.inspectTargets(job.operations.map((operation) => operation.targetId));
    const missing = inspections.find((inspection) => !inspection.found || !inspection.editable);
    if (!missing) {
      return;
    }

    throw new UpdateFailure(
      missing.found ? "TARGET_NOT_EDITABLE" : "TARGET_NOT_FOUND",
      `Target ${missing.targetId} is not available for editing.`,
      missing.message,
    );
  }
}

export function resultFromFailure(
  job: UpdateJob,
  error: unknown,
  fallbackCode: UpdateErrorCode = "UPDATE_FAILED",
  startedAt = new Date().toISOString(),
): UpdateResult {
  return {
    jobId: job.jobId,
    status: "failed",
    startedAt,
    finishedAt: new Date().toISOString(),
    targets: job.operations.map<TargetResult>((operation) => ({
      targetId: operation.targetId,
      operationKind: operation.kind,
      status: "failed",
      error: toUpdateError(error, fallbackCode),
    })),
  };
}

function normalizeUrl(value?: string): string {
  return (value ?? "").trim().replace(/\/+$/u, "").toLowerCase();
}
