import type { UpdateError, UpdateErrorCode } from "./types";

export class UpdateFailure extends Error {
  readonly updateError: UpdateError;

  constructor(code: UpdateErrorCode, operatorMessage: string, technicalMessage?: string, retryable = false) {
    super(operatorMessage);
    this.name = "UpdateFailure";
    this.updateError = {
      code,
      retryable,
      operatorMessage,
      technicalMessage,
    };
  }
}

export function toUpdateError(error: unknown, fallbackCode: UpdateErrorCode): UpdateError {
  if (error instanceof UpdateFailure) {
    return error.updateError;
  }

  if (error instanceof Error) {
    return {
      code: fallbackCode,
      retryable: false,
      operatorMessage: fallbackOperatorMessage(fallbackCode),
      technicalMessage: error.name,
    };
  }

  return {
    code: fallbackCode,
    retryable: false,
    operatorMessage: fallbackOperatorMessage(fallbackCode),
    technicalMessage: typeof error,
  };
}

function fallbackOperatorMessage(code: UpdateErrorCode): string {
  switch (code) {
    case "ARTIFACT_FETCH_FAILED":
      return "Artifact fetch failed.";
    case "JOB_API_FAILED":
      return "Job API request failed.";
    case "OFFICE_SYNC_FAILED":
      return "Office update failed.";
    case "OPERATOR_EDIT_FAILED":
      return "PowerPoint edit failed.";
    case "VM_OPERATOR_UNAVAILABLE":
      return "Windows Operator is unavailable.";
    default:
      return "Update failed.";
  }
}
