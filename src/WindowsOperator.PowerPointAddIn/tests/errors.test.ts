import { describe, expect, it } from "vitest";
import { toUpdateError, UpdateFailure } from "../src/domain/errors";

describe("error translation", () => {
  it("preserves stable UpdateFailure messages", () => {
    const error = toUpdateError(
      new UpdateFailure("TARGET_NOT_FOUND", "Target missing.", "shape lookup failed"),
      "UPDATE_FAILED",
    );

    expect(error).toMatchObject({
      code: "TARGET_NOT_FOUND",
      operatorMessage: "Target missing.",
      technicalMessage: "shape lookup failed",
    });
  });

  it("hides raw generic error messages from operator-facing text", () => {
    const error = toUpdateError(new Error("https://example.invalid?token=secret"), "JOB_API_FAILED");

    expect(error.operatorMessage).toBe("Job API request failed.");
    expect(error.operatorMessage).not.toContain("secret");
    expect(error.technicalMessage).toBe("Error");
  });
});
