import { toUpdateError, UpdateFailure } from "../domain/errors";
import type {
  DiscoveredTarget,
  ResolvedArtifact,
  TargetInspection,
  TargetResult,
  UpdateErrorCode,
  UpdateJob,
  UpdateResult,
} from "../domain/types";
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
    const boundTargets = await this.bindNamedTargets(job);
    const discoveredTargets = await this.discoverTargets(job);
    const inspections = boundTargets ?? await this.inspectTargets(job);

    if (job.validateOnly) {
      const targets = toValidationResults(job, inspections);
      return {
        jobId: job.jobId,
        status: targets.some((target) => target.status === "failed") ? "failed" : "succeeded",
        startedAt,
        finishedAt: new Date().toISOString(),
        targets,
        discoveredTargets,
      };
    }

    if (job.operations.length === 0) {
      return {
        jobId: job.jobId,
        status: "succeeded",
        startedAt,
        finishedAt: new Date().toISOString(),
        targets: [],
        discoveredTargets,
      };
    }

    const artifactMap = await this.resolveArtifacts(job);
    this.assertInspectionsEditable(inspections);
    const targets = mergeInspectionMetadata(await this.presentation.apply(job.operations, artifactMap), inspections);

    const failedTarget = targets.find((target) => target.status === "failed");

    return {
      jobId: job.jobId,
      status: failedTarget ? "failed" : "succeeded",
      startedAt,
      finishedAt: new Date().toISOString(),
      targets,
      discoveredTargets,
    };
  }

  private assertDocumentMatch(job: UpdateJob): void {
    if (!job.expectedDocumentUrl) {
      return;
    }

    if (!documentsMatch(job.expectedDocumentUrl, this.currentDocument.getUrl())) {
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
      if (!operation.artifact) {
        throw new UpdateFailure("ARTIFACT_NOT_FOUND", `Image target ${operation.targetId} is missing artifact.`);
      }
      if (resolved.has(operation.artifact.artifactId)) {
        continue;
      }
      const artifact = await this.artifacts.resolve(operation.artifact);
      resolved.set(artifact.artifactId, artifact);
    }
    return resolved;
  }

  private async inspectTargets(job: UpdateJob): Promise<TargetInspection[]> {
    return this.presentation.inspectTargets(job.operations.map((operation) => operation.targetId));
  }

  private async bindNamedTargets(job: UpdateJob): Promise<TargetInspection[] | undefined> {
    if (!job.bindNamedTargets || job.operations.length === 0) {
      return undefined;
    }

    return this.presentation.bindNamedTargets(job.operations);
  }

  private async discoverTargets(job: UpdateJob): Promise<DiscoveredTarget[] | undefined> {
    if (!job.discoverTargets) {
      return undefined;
    }

    return this.presentation.discoverTargets();
  }

  private assertInspectionsEditable(inspections: TargetInspection[]): void {
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

function mergeInspectionMetadata(targets: TargetResult[], inspections: TargetInspection[]): TargetResult[] {
  return targets.map((target) => {
    const inspection = inspections.find((candidate) => candidate.targetId === target.targetId);
    if (!inspection) {
      return target;
    }

    return {
      ...target,
      found: target.found ?? inspection.found,
      editable: target.editable ?? inspection.editable,
      type: target.type ?? inspection.type,
      message: target.message ?? inspection.message,
      shapeName: target.shapeName ?? inspection.shapeName,
      source: inspection.source ?? target.source,
      bound: inspection.bound ?? target.bound,
      tagged: inspection.tagged ?? target.tagged,
    };
  });
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

function toValidationResults(job: UpdateJob, inspections: TargetInspection[]): TargetResult[] {
  return job.operations.map((operation) => {
    const inspection = inspections.find((candidate) => candidate.targetId === operation.targetId);
    if (!inspection || !inspection.found || !inspection.editable) {
      return {
        targetId: operation.targetId,
        operationKind: operation.kind,
        status: "failed",
        error: toUpdateError(
          new UpdateFailure(
            inspection?.found ? "TARGET_NOT_EDITABLE" : "TARGET_NOT_FOUND",
            `Target ${operation.targetId} is not available for editing.`,
            inspection?.message,
          ),
          "UPDATE_FAILED",
        ),
        found: inspection?.found ?? false,
        editable: inspection?.editable ?? false,
        type: inspection?.type,
        message: inspection?.message,
        shapeName: inspection?.shapeName,
        source: inspection?.source,
        bound: inspection?.bound,
        tagged: inspection?.tagged,
      };
    }

    return {
      targetId: operation.targetId,
      operationKind: operation.kind,
      status: "skipped",
      found: inspection.found,
      editable: inspection.editable,
      type: inspection.type,
      message: inspection.message,
      shapeName: inspection.shapeName,
      source: inspection.source,
      bound: inspection.bound,
      tagged: inspection.tagged,
    };
  });
}

function documentsMatch(expected?: string, actual?: string): boolean {
  if (!expected || !actual) {
    return true;
  }

  const expectedIdentity = documentIdentity(expected);
  const actualIdentity = documentIdentity(actual);
  if (expectedIdentity.normalizedUrl === actualIdentity.normalizedUrl) {
    return true;
  }

  if (expectedIdentity.sourceDoc && expectedIdentity.sourceDoc === actualIdentity.sourceDoc) {
    return true;
  }

  return Boolean(
    expectedIdentity.host &&
      expectedIdentity.host === actualIdentity.host &&
      expectedIdentity.fileName &&
      expectedIdentity.fileName === actualIdentity.fileName,
  );
}

function documentIdentity(value: string): {
  normalizedUrl: string;
  host?: string;
  sourceDoc?: string;
  fileName?: string;
} {
  try {
    const url = new URL(value.trim());
    const query = [...url.searchParams.entries()]
      .map(([key, queryValue]) => [key.toLowerCase(), queryValue] as const);
    const normalizedQuery = query
      .filter(([key]) => !["action", "mobileredirect", "web"].includes(key))
      .sort(([leftKey, leftValue], [rightKey, rightValue]) =>
        leftKey === rightKey ? leftValue.localeCompare(rightValue) : leftKey.localeCompare(rightKey),
      )
      .map(([key, queryValue]) => `${encodeURIComponent(key)}=${encodeURIComponent(queryValue)}`)
      .join("&");
    const path = url.pathname.replace(/\/+$/u, "") || "/";
    const normalizedUrl =
      `${url.protocol.toLowerCase()}//${url.host.toLowerCase()}${path.toLowerCase()}` +
      (normalizedQuery ? `?${normalizedQuery}` : "");
    const sourceDoc = url.searchParams.get("sourcedoc")?.replace(/[{}]/gu, "").toLowerCase();
    const fileName = normalizeFileName(url.searchParams.get("file") ?? decodeURIComponent(path.split("/").pop() ?? ""));

    return {
      normalizedUrl,
      host: url.host.toLowerCase(),
      sourceDoc,
      fileName,
    };
  } catch {
    return {
      normalizedUrl: value.trim().replace(/\/+$/u, "").toLowerCase(),
    };
  }
}

function normalizeFileName(value?: string | null): string | undefined {
  const normalized = value?.trim().toLowerCase();
  return normalized ? normalized : undefined;
}
