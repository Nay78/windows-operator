import type { CurrentDocumentProvider } from "../ports";

export class OfficeCurrentDocumentProvider implements CurrentDocumentProvider {
  getUrl(): string | undefined {
    if (typeof Office === "undefined") {
      return undefined;
    }

    return Office.context.document.url || undefined;
  }
}
