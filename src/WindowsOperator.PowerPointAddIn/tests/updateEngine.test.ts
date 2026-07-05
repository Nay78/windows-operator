import { describe, expect, it } from "vitest";
import type {
  ArtifactRef,
  DiscoveredTarget,
  ResolvedArtifact,
  TargetInspection,
  TargetResult,
  UpdateJob,
  UpdateOperation,
} from "../src/domain/types";
import type { ArtifactResolver, PresentationAdapter } from "../src/ports";
import { UpdateEngine } from "../src/update/updateEngine";

class FakeArtifacts implements ArtifactResolver {
  resolveCalls = 0;

  async resolve(ref: ArtifactRef): Promise<ResolvedArtifact> {
    this.resolveCalls += 1;
    return {
      artifactId: ref.artifactId,
      mediaType: ref.mediaType,
      base64: "abc",
      byteLength: 3,
    };
  }
}

class FakePresentation implements PresentationAdapter {
  applyCalls = 0;
  bindNamedTargetCalls = 0;
  discoverCalls = 0;

  constructor(
    private readonly inspections: TargetInspection[],
    private readonly discoveredTargets: DiscoveredTarget[] = [],
    private readonly boundInspections: TargetInspection[] = [],
  ) {}

  async discoverTargets(): Promise<DiscoveredTarget[]> {
    this.discoverCalls += 1;
    return this.discoveredTargets;
  }

  async inspectTargets(): Promise<TargetInspection[]> {
    return this.inspections;
  }

  async bindNamedTargets(): Promise<TargetInspection[]> {
    this.bindNamedTargetCalls += 1;
    return this.boundInspections;
  }

  async apply(operations: UpdateOperation[]): Promise<TargetResult[]> {
    this.applyCalls += 1;
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

  it("matches SharePoint canonical URL to Office document path", async () => {
    const engine = new UpdateEngine(
      new FakePresentation([{ targetId: "TITLE_MAIN", found: true, editable: true }]),
      new FakeArtifacts(),
      {
        getUrl: () =>
          "https://tenant.sharepoint.com/personal/user/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
      },
    );

    const result = await engine.apply({
      ...baseJob,
      expectedDocumentUrl:
        "https://tenant.sharepoint.com/:p:/r/personal/user/_layouts/15/Doc.aspx?sourcedoc=%7BBA878CDB-CE08-495B-BB23-6B8FFC5DBB25%7D&file=SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx&action=edit&mobileredirect=true",
    });

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

  it("validateOnly skips artifacts and mutation when targets are editable", async () => {
    const presentation = new FakePresentation([{ targetId: "TITLE_MAIN", found: true, editable: true, type: "text" }]);
    const artifacts = new FakeArtifacts();
    const engine = new UpdateEngine(
      presentation,
      artifacts,
      { getUrl: () => "https://example.invalid/deck.pptx" },
    );

    const result = await engine.apply({
      ...baseJob,
      validateOnly: true,
      operations: [
        {
          kind: "replaceImage",
          targetId: "TITLE_MAIN",
          fit: "contain",
        },
      ],
    });

    expect(result.status).toBe("succeeded");
    expect(result.targets).toEqual([
      expect.objectContaining({
        targetId: "TITLE_MAIN",
        status: "skipped",
        found: true,
        editable: true,
        type: "text",
      }),
    ]);
    expect(artifacts.resolveCalls).toBe(0);
    expect(presentation.applyCalls).toBe(0);
  });

  it("discoverTargets returns adapter discovery without mutation", async () => {
    const presentation = new FakePresentation(
      [],
      [
        { targetId: "TITLE_MAIN", editable: true, type: "text", message: "Tagged binding." },
        { targetId: "DATA_TABLE", editable: true, type: "table", message: "Tagged table binding." },
      ],
    );
    const engine = new UpdateEngine(
      presentation,
      new FakeArtifacts(),
      { getUrl: () => "https://example.invalid/deck.pptx" },
    );

    const result = await engine.apply({
      ...baseJob,
      discoverTargets: true,
      operations: [],
    });

    expect(result.status).toBe("succeeded");
    expect(result.targets).toEqual([]);
    expect(result.discoveredTargets).toEqual([
      { targetId: "TITLE_MAIN", editable: true, type: "text", message: "Tagged binding." },
      { targetId: "DATA_TABLE", editable: true, type: "table", message: "Tagged table binding." },
    ]);
    expect(presentation.discoverCalls).toBe(1);
    expect(presentation.applyCalls).toBe(0);
  });

  it("applies table operations without resolving image artifacts", async () => {
    const presentation = new FakePresentation([{ targetId: "DATA_TABLE", found: true, editable: true, type: "table" }]);
    const artifacts = new FakeArtifacts();
    const engine = new UpdateEngine(
      presentation,
      artifacts,
      { getUrl: () => "https://example.invalid/deck.pptx" },
    );

    const result = await engine.apply({
      ...baseJob,
      operations: [
        {
          kind: "replaceTableCell",
          targetId: "DATA_TABLE",
          rowIndex: 1,
          columnIndex: 2,
          text: "42",
        },
      ],
    });

    expect(result.status).toBe("succeeded");
    expect(artifacts.resolveCalls).toBe(0);
    expect(presentation.applyCalls).toBe(1);
  });

  it("binds named targets before inspection when requested", async () => {
    const presentation = new FakePresentation(
      [{ targetId: "TITLE_MAIN", found: false, editable: false }],
      [],
      [
        {
          targetId: "TITLE_MAIN",
          found: true,
          editable: true,
          type: "text",
          shapeName: "TARGET_TITLE_MAIN",
          source: "repairedName",
          bound: true,
          tagged: true,
        },
      ],
    );
    const engine = new UpdateEngine(
      presentation,
      new FakeArtifacts(),
      { getUrl: () => "https://example.invalid/deck.pptx" },
    );

    const result = await engine.apply({
      ...baseJob,
      bindNamedTargets: true,
    });

    expect(result.status).toBe("succeeded");
    expect(presentation.bindNamedTargetCalls).toBe(1);
    expect(result.targets).toEqual([
      expect.objectContaining({
        targetId: "TITLE_MAIN",
        status: "succeeded",
        shapeName: "TARGET_TITLE_MAIN",
        source: "repairedName",
        bound: true,
        tagged: true,
      }),
    ]);
  });

  it("validateOnly reports named repair metadata without applying", async () => {
    const presentation = new FakePresentation(
      [],
      [],
      [
        {
          targetId: "TITLE_MAIN",
          found: true,
          editable: true,
          type: "text",
          shapeName: "TARGET_TITLE_MAIN",
          source: "repairedName",
          bound: true,
          tagged: true,
        },
      ],
    );
    const engine = new UpdateEngine(
      presentation,
      new FakeArtifacts(),
      { getUrl: () => "https://example.invalid/deck.pptx" },
    );

    const result = await engine.apply({
      ...baseJob,
      bindNamedTargets: true,
      validateOnly: true,
    });

    expect(result.status).toBe("succeeded");
    expect(result.targets).toEqual([
      expect.objectContaining({
        targetId: "TITLE_MAIN",
        status: "skipped",
        shapeName: "TARGET_TITLE_MAIN",
        source: "repairedName",
        bound: true,
        tagged: true,
      }),
    ]);
    expect(presentation.applyCalls).toBe(0);
  });

  it("discoverTargets also keeps validation results when operations exist", async () => {
    const presentation = new FakePresentation(
      [{ targetId: "TITLE_MAIN", found: true, editable: true, type: "text" }],
      [{ targetId: "TITLE_MAIN", editable: true, type: "text" }],
    );
    const engine = new UpdateEngine(
      presentation,
      new FakeArtifacts(),
      { getUrl: () => "https://example.invalid/deck.pptx" },
    );

    const result = await engine.apply({
      ...baseJob,
      discoverTargets: true,
      validateOnly: true,
    });

    expect(result.status).toBe("succeeded");
    expect(result.targets).toEqual([
      expect.objectContaining({
        targetId: "TITLE_MAIN",
        status: "skipped",
      }),
    ]);
    expect(result.discoveredTargets).toEqual([
      { targetId: "TITLE_MAIN", editable: true, type: "text" },
    ]);
  });

  it("validateOnly reports failed targets without throwing", async () => {
    const engine = new UpdateEngine(
      new FakePresentation([{ targetId: "TITLE_MAIN", found: false, editable: false, message: "Binding not found." }]),
      new FakeArtifacts(),
      { getUrl: () => "https://example.invalid/deck.pptx" },
    );

    const result = await engine.apply({
      ...baseJob,
      validateOnly: true,
      operations: [
        {
          kind: "replaceText",
          targetId: "TITLE_MAIN",
          mode: "plain",
        },
      ],
    });

    expect(result.status).toBe("failed");
    expect(result.targets).toEqual([
      expect.objectContaining({
        targetId: "TITLE_MAIN",
        status: "failed",
        found: false,
        editable: false,
        message: "Binding not found.",
        error: expect.objectContaining({
          code: "TARGET_NOT_FOUND",
        }),
      }),
    ]);
  });
});
