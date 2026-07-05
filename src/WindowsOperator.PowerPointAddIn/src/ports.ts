import type {
  ArtifactRef,
  DiscoveredTarget,
  ResolvedArtifact,
  TargetInspection,
  UpdateError,
  UpdateJob,
  UpdateOperation,
  UpdateResult,
} from "./domain/types";

export interface UpdateJobClient {
  claimNextJob(documentUrl?: string): Promise<UpdateJob | null>;
  complete(result: UpdateResult): Promise<void>;
  fail(jobId: string, error: UpdateError): Promise<void>;
}

export interface ArtifactResolver {
  resolve(ref: ArtifactRef): Promise<ResolvedArtifact>;
}

export interface PresentationAdapter {
  discoverTargets(): Promise<DiscoveredTarget[]>;
  bindNamedTargets(operations: UpdateOperation[]): Promise<TargetInspection[]>;
  inspectTargets(targetIds: string[]): Promise<TargetInspection[]>;
  apply(operations: UpdateOperation[], artifacts: Map<string, ResolvedArtifact>): Promise<UpdateResult["targets"]>;
}

export interface CurrentDocumentProvider {
  getUrl(): string | undefined;
}
