import { UpdateFailure } from "../domain/errors";
import type { UpdateError, UpdateJob, UpdateResult } from "../domain/types";
import type { UpdateJobClient } from "../ports";

export class HttpJobClient implements UpdateJobClient {
  constructor(private readonly baseUrl = "") {}

  async claimNextJob(documentUrl?: string): Promise<UpdateJob | null> {
    const response = await postJson(`${this.baseUrl}/v1/powerpoint/jobs/claim`, {
      workerId: "officejs-taskpane",
      documentUrl,
    });
    if (response.status === 204) {
      return null;
    }
    if (!response.ok) {
      throw new UpdateFailure("JOB_API_FAILED", "Job claim failed.", `status=${response.status}`, response.status >= 500);
    }
    return response.json() as Promise<UpdateJob>;
  }

  async complete(result: UpdateResult): Promise<void> {
    await this.post(`/v1/powerpoint/jobs/${encodeURIComponent(result.jobId)}/complete`, result);
  }

  async fail(jobId: string, error: UpdateError): Promise<void> {
    await this.post(`/v1/powerpoint/jobs/${encodeURIComponent(jobId)}/fail`, error);
  }

  private async post(path: string, body: unknown): Promise<void> {
    const response = await postJson(`${this.baseUrl}${path}`, body);
    if (!response.ok) {
      throw new UpdateFailure("JOB_API_FAILED", "Job API POST failed.", `status=${response.status}`, response.status >= 500);
    }
  }
}

async function postJson(url: string, body?: unknown): Promise<Response> {
  try {
    return await fetch(url, {
      method: "POST",
      headers: body === undefined ? undefined : { "content-type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch (error) {
    throw new UpdateFailure(
      "JOB_API_FAILED",
      "Job API request failed.",
      error instanceof Error ? error.name : typeof error,
      true,
    );
  }
}
