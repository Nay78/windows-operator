import { describe, expect, it } from "vitest";
import type {
  ArtifactRef,
  ResolvedArtifact,
  TargetInspection,
  TargetResult,
  UpdateJob,
  UpdateOperation,
} from "../src/domain/types";
import type { ArtifactResolver, PresentationAdapter } from "../src/ports";
import { UpdateEngine } from "../src/update/updateEngine";

class FakeArtifacts implements ArtifactResolver {
  async resolve(ref: ArtifactRef): Promise<ResolvedArtifact> {
    return {
      artifactId: ref.artifactId,
      mediaType: ref.mediaType,
      base64: "abc",
      byteLength: 3,
    };
  }
}

class FakePresentation implements PresentationAdapter {
  constructor(private readonly inspections: TargetInspection[]) {}

  async inspectTargets(): Promise<TargetInspection[]> {
    return this.inspections;
  }

  async apply(operations: UpdateOperation[]): Promise<TargetResult[]> {
    return operations.map((operation) => ({
      targetId: operation.targetId,
      operationKind: operation.kind,
      status: "succeeded",
    }));
  }
}

const baseJob: UpdateJob = {
  jobId: "job-1",
  expectedDocumentUrl: "https://example.invalid/deck.pptx",
  requestedBy: "test",
  createdAt: "2026-06-16T00:00:00.000Z",
  operations: [
    {
      kind: "replaceText",
      targetId: "TITLE_MAIN",
      mode: "plain",
      text: "Hello",
    },
  ],
};

describe("UpdateEngine", () => {
  it("applies job when targets exist", async () => {
    const engine = new UpdateEngine(
      new FakePresentation([{ targetId: "TITLE_MAIN", found: true, editable: true }]),
      new FakeArtifacts(),
      { getUrl: () => "https://example.invalid/deck.pptx" },
    );

    const result = await engine.apply(baseJob);

    expect(result.status).toBe("succeeded");
  });

  it("fails before mutation when target is missing", async () => {
    const engine = new UpdateEngine(
      new FakePresentation([{ targetId: "TITLE_MAIN", found: false, editable: false }]),
      new FakeArtifacts(),
      { getUrl: () => "https://example.invalid/deck.pptx" },
    );

    await expect(engine.apply(baseJob)).rejects.toMatchObject({
      updateError: {
        code: "TARGET_NOT_FOUND",
      },
    });
  });

  it("fails before mutation when active document does not match", async () => {
    const engine = new UpdateEngine(
      new FakePresentation([{ targetId: "TITLE_MAIN", found: true, editable: true }]),
      new FakeArtifacts(),
      { getUrl: () => "https://example.invalid/other.pptx" },
    );

    await expect(engine.apply(baseJob)).rejects.toMatchObject({
      updateError: {
        code: "FILE_NOT_EDITABLE",
      },
    });
  });
});
