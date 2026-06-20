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
});
