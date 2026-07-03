import { UpdateFailure } from "../domain/errors";
import type { ArtifactRef, ResolvedArtifact } from "../domain/types";
import type { ArtifactResolver } from "../ports";

const MAX_ARTIFACT_BYTES = 15 * 1024 * 1024;

export class HttpArtifactResolver implements ArtifactResolver {
  async resolve(ref: ArtifactRef): Promise<ResolvedArtifact> {
    if (!ref.url) {
      throw new UpdateFailure("ARTIFACT_NOT_FOUND", `Artifact ${ref.artifactId} has no URL.`);
    }

    if (ref.expiresAt && Date.parse(ref.expiresAt) < Date.now()) {
      throw new UpdateFailure("ARTIFACT_INVALID", `Artifact ${ref.artifactId} URL expired.`);
    }

    const bytes = ref.url.startsWith("data:")
      ? readDataUrl(ref.url, ref.mediaType)
      : await fetchBytes(ref.url, ref.mediaType);

    if (bytes.byteLength === 0 || bytes.byteLength > MAX_ARTIFACT_BYTES) {
      throw new UpdateFailure(
        "ARTIFACT_INVALID",
        `Artifact ${ref.artifactId} size is outside allowed bounds.`,
        `byteLength=${bytes.byteLength}`,
      );
    }

    const sha256 = await digestSha256(bytes);
    if (ref.sha256 && sha256 !== ref.sha256) {
      throw new UpdateFailure(
        "ARTIFACT_INVALID",
        `Artifact ${ref.artifactId} checksum mismatch.`,
        `expected=${ref.sha256}; actual=${sha256}`,
      );
    }

    return {
      artifactId: ref.artifactId,
      mediaType: ref.mediaType,
      base64: bytesToBase64(bytes),
      byteLength: bytes.byteLength,
      sha256,
    };
  }
}

async function fetchBytes(url: string, expectedMediaType: ArtifactRef["mediaType"]): Promise<Uint8Array> {
  let response: Response;
  try {
    response = await fetch(url);
  } catch (error) {
    throw new UpdateFailure("ARTIFACT_FETCH_FAILED", "Artifact fetch failed.", describeUnknown(error), true);
  }

  if (!response.ok) {
    throw new UpdateFailure(
      "ARTIFACT_FETCH_FAILED",
      "Artifact fetch failed.",
      `status=${response.status}; source=${describeArtifactSource(url)}`,
      response.status >= 500,
    );
  }

  const contentType = response.headers.get("content-type")?.split(";")[0]?.trim().toLowerCase();
  if (contentType && contentType !== expectedMediaType) {
    throw new UpdateFailure(
      "ARTIFACT_INVALID",
      "Artifact MIME type does not match job payload.",
      `expected=${expectedMediaType}; actual=${contentType}`,
    );
  }

  return new Uint8Array(await response.arrayBuffer());
}

function readDataUrl(url: string, expectedMediaType: ArtifactRef["mediaType"]): Uint8Array {
  const match = /^data:([^;,]+);base64,(.*)$/u.exec(url);
  if (!match) {
    throw new UpdateFailure("ARTIFACT_INVALID", "Artifact data URL must be base64 encoded.");
  }

  const [, mediaType, payload] = match;
  if (mediaType !== expectedMediaType) {
    throw new UpdateFailure(
      "ARTIFACT_INVALID",
      "Artifact data URL MIME type does not match job payload.",
      `expected=${expectedMediaType}; actual=${mediaType}`,
    );
  }

  const binary = atob(payload);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }
  return bytes;
}

function bytesToBase64(bytes: Uint8Array): string {
  const chunkSize = 0x8000;
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    const chunk = bytes.subarray(offset, offset + chunkSize);
    binary += String.fromCharCode(...chunk);
  }
  return btoa(binary);
}

async function digestSha256(bytes: Uint8Array): Promise<string> {
  const digestInput = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
  const digest = await crypto.subtle.digest("SHA-256", digestInput);
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

function describeArtifactSource(url: string): "data-url" | "remote-url" {
  return url.startsWith("data:") ? "data-url" : "remote-url";
}

function describeUnknown(error: unknown): string {
  return error instanceof Error ? error.name : typeof error;
}
