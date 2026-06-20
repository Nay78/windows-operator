import type { UpdateError, UpdateJob, UpdateResult } from "../domain/types";
import type { UpdateJobClient } from "../ports";

const SAMPLE_PNG_DATA_URL =
  "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=";

export class MockJobClient implements UpdateJobClient {
  private claimed = false;
  private readonly onLog: (line: string) => void;

  constructor(onLog: (line: string) => void = console.log) {
    this.onLog = onLog;
  }

  async claimNextJob(): Promise<UpdateJob | null> {
    if (this.claimed) {
      return null;
    }

    this.claimed = true;
    return {
      jobId: "mock-job-001",
      requestedBy: "local-dev",
      createdAt: new Date().toISOString(),
      operations: [
        {
          kind: "replaceText",
          targetId: "TITLE_MAIN",
          text: "Updated from mock job",
          mode: "plain",
        },
        {
          kind: "replaceImage",
          targetId: "HERO_IMAGE",
          artifact: {
            artifactId: "mock-pixel",
            url: SAMPLE_PNG_DATA_URL,
            mediaType: "image/png",
          },
          altText: "Generated mock image.",
          fit: "cover",
        },
      ],
    };
  }

  async complete(result: UpdateResult): Promise<void> {
    this.onLog(`complete:${JSON.stringify(result)}`);
  }

  async fail(jobId: string, error: UpdateError): Promise<void> {
    this.onLog(`fail:${jobId}:${JSON.stringify(error)}`);
  }
}
