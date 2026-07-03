import { afterEach, describe, expect, it, vi } from "vitest";
import { HttpArtifactResolver } from "../src/artifacts/httpArtifactResolver";

describe("HttpArtifactResolver", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("resolves base64 data URLs", async () => {
    const resolver = new HttpArtifactResolver();
    const artifact = await resolver.resolve({
      artifactId: "pixel",
      mediaType: "image/png",
      url: "data:image/png;base64,iVBORw0KGgo=",
    });

    expect(artifact.artifactId).toBe("pixel");
    expect(artifact.mediaType).toBe("image/png");
    expect(artifact.byteLength).toBeGreaterThan(0);
    expect(artifact.base64).toBe("iVBORw0KGgo=");
    expect(artifact.sha256).toMatch(/^[0-9a-f]{64}$/u);
  });

  it("rejects MIME mismatches", async () => {
    const resolver = new HttpArtifactResolver();

    await expect(
      resolver.resolve({
        artifactId: "wrong",
        mediaType: "image/jpeg",
        url: "data:image/png;base64,iVBORw0KGgo=",
      }),
    ).rejects.toMatchObject({
      updateError: {
        code: "ARTIFACT_INVALID",
      },
    });
  });

  it("accepts fetched artifact MIME type case-insensitively", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(new Uint8Array([1, 2, 3]), { headers: { "content-type": "IMAGE/PNG; charset=binary" } })),
    );
    const resolver = new HttpArtifactResolver();

    const artifact = await resolver.resolve({
      artifactId: "remote",
      mediaType: "image/png",
      url: "https://artifacts.example.invalid/image.png",
    });

    expect(artifact.byteLength).toBe(3);
  });

  it("does not include signed artifact URLs in fetch failure details", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("nope", { status: 403 })));
    const resolver = new HttpArtifactResolver();
    const signedUrl = "https://artifacts.example.invalid/image.png?sig=secret-token";

    await expect(
      resolver.resolve({
        artifactId: "signed",
        mediaType: "image/png",
        url: signedUrl,
      }),
    ).rejects.toMatchObject({
      updateError: {
        code: "ARTIFACT_FETCH_FAILED",
        technicalMessage: expect.not.stringContaining("secret-token"),
      },
    });
  });

  it("rejects checksum mismatches", async () => {
    const resolver = new HttpArtifactResolver();

    await expect(
      resolver.resolve({
        artifactId: "pixel",
        mediaType: "image/png",
        url: "data:image/png;base64,iVBORw0KGgo=",
        sha256: "0".repeat(64),
      }),
    ).rejects.toMatchObject({
      updateError: {
        code: "ARTIFACT_INVALID",
        operatorMessage: expect.stringContaining("checksum mismatch"),
        technicalMessage: expect.stringContaining("expected="),
      },
    });
  });

  it("rejects empty artifacts", async () => {
    const resolver = new HttpArtifactResolver();

    await expect(
      resolver.resolve({
        artifactId: "empty",
        mediaType: "image/png",
        url: "data:image/png;base64,",
      }),
    ).rejects.toMatchObject({
      updateError: {
        code: "ARTIFACT_INVALID",
        technicalMessage: "byteLength=0",
      },
    });
  });

  it("rejects oversized artifacts", async () => {
    const oversizedBytes = new Uint8Array(15 * 1024 * 1024 + 1);
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(oversizedBytes, { headers: { "content-type": "image/png" } })),
    );
    const resolver = new HttpArtifactResolver();

    await expect(
      resolver.resolve({
        artifactId: "oversized",
        mediaType: "image/png",
        url: "https://artifacts.example.invalid/large.png",
      }),
    ).rejects.toMatchObject({
      updateError: {
        code: "ARTIFACT_INVALID",
        technicalMessage: `byteLength=${oversizedBytes.byteLength}`,
      },
    });
  });
});
