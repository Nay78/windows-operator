import { afterEach, describe, expect, it, vi } from "vitest";
import { HttpJobClient } from "../src/jobs/httpJobClient";

describe("HttpJobClient", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("claims jobs with the document URL and treats 204 as no work", async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);
    const client = new HttpJobClient("https://operator.example");

    await expect(client.claimNextJob("https://docs.example/deck.pptx")).resolves.toBeNull();

    expect(fetchMock).toHaveBeenCalledWith(
      "https://operator.example/v1/powerpoint/jobs/claim",
      expect.objectContaining({
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          workerId: "officejs-taskpane",
          documentUrl: "https://docs.example/deck.pptx",
        }),
      }),
    );
  });

  it("normalizes a trailing slash in the base URL", async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);
    const client = new HttpJobClient("https://operator.example/");

    await client.claimNextJob();

    expect(fetchMock).toHaveBeenCalledWith(
      "https://operator.example/v1/powerpoint/jobs/claim",
      expect.any(Object),
    );
  });

  it("escapes job IDs in terminal update paths", async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const client = new HttpJobClient();
    const error = {
      code: "UPDATE_FAILED" as const,
      retryable: false,
      operatorMessage: "Update failed.",
    };

    await client.fail("job/with?chars", error);

    expect(fetchMock).toHaveBeenCalledWith(
      "/v1/powerpoint/jobs/job%2Fwith%3Fchars/fail",
      expect.objectContaining({
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(error),
      }),
    );
  });

  it("marks transient job API failures as retryable", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("busy", { status: 503 })));
    const client = new HttpJobClient();

    await expect(client.claimNextJob()).rejects.toMatchObject({
      updateError: {
        code: "JOB_API_FAILED",
        retryable: true,
        technicalMessage: "status=503",
      },
    });
  });
});
