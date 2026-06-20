import { UpdateFailure } from "../domain/errors";

export function assertPowerPointRequirements(): void {
  if (!globalThis.Office || !Office.context?.requirements) {
    throw new UpdateFailure("HOST_NOT_READY", "Office host is not ready.");
  }

  if (!Office.context.requirements.isSetSupported("PowerPointApi", "1.8")) {
    throw new UpdateFailure("API_UNSUPPORTED", "PowerPointApi 1.8 is required for update jobs.");
  }
}

export function supportsPowerPointApi(version: string): boolean {
  return Boolean(globalThis.Office?.context?.requirements?.isSetSupported("PowerPointApi", version));
}
