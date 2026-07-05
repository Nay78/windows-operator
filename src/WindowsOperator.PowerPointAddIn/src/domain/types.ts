export type JobStatus = "queued" | "running" | "succeeded" | "failed" | "partial";

export interface UpdateJob {
  jobId: string;
  expectedDocumentUrl?: string;
  discoverTargets?: boolean;
  bindNamedTargets?: boolean;
  validateOnly?: boolean;
  operations: UpdateOperation[];
  requestedBy: string;
  createdAt: string;
}

export type TargetType = "text" | "image" | "table" | "unknown";
export type TargetSource = "binding" | "name" | "repairedName";

export type UpdateOperation =
  | TextUpdateOperation
  | ImageUpdateOperation
  | ReadTableOperation
  | TableCellUpdateOperation
  | TableRangeUpdateOperation;

export interface TextUpdateOperation {
  kind: "replaceText";
  targetId: string;
  text?: string;
  mode: "plain";
  allowEmpty?: boolean;
}

export interface ImageUpdateOperation {
  kind: "replaceImage";
  targetId: string;
  artifact?: ArtifactRef;
  altText?: string;
  fit?: "cover" | "contain";
}

export interface ReadTableOperation {
  kind: "readTable";
  targetId: string;
}

export interface TableCellUpdateOperation {
  kind: "replaceTableCell";
  targetId: string;
  rowIndex?: number;
  columnIndex?: number;
  text?: string;
  allowEmpty?: boolean;
}

export interface TableRangeUpdateOperation {
  kind: "replaceTableRange";
  targetId: string;
  startRowIndex?: number;
  startColumnIndex?: number;
  values?: string[][];
  allowEmpty?: boolean;
}

export interface ArtifactRef {
  artifactId: string;
  url?: string;
  mediaType: "image/png" | "image/jpeg";
  sha256?: string;
  expiresAt?: string;
}

export interface ResolvedArtifact {
  artifactId: string;
  mediaType: "image/png" | "image/jpeg";
  base64: string;
  path?: string;
  windowsPath?: string;
  byteLength: number;
  sha256?: string;
  width?: number;
  height?: number;
}

export interface UpdateResult {
  jobId: string;
  status: JobStatus;
  startedAt: string;
  finishedAt: string;
  targets: TargetResult[];
  discoveredTargets?: DiscoveredTarget[];
}

export interface TargetResult {
  targetId: string;
  operationKind: UpdateOperation["kind"];
  status: "succeeded" | "failed" | "skipped";
  error?: UpdateError;
  found?: boolean;
  editable?: boolean;
  type?: TargetType;
  message?: string;
  shapeName?: string;
  source?: TargetSource;
  bound?: boolean;
  tagged?: boolean;
  table?: TableSnapshot;
}

export interface DiscoveredTarget {
  targetId: string;
  editable: boolean;
  type: TargetType;
  message?: string;
  shapeName?: string;
  source?: TargetSource;
  bound?: boolean;
  tagged?: boolean;
}

export interface TableSnapshot {
  rowCount: number;
  columnCount: number;
  values: string[][];
}

export interface UpdateError {
  code: UpdateErrorCode;
  retryable: boolean;
  operatorMessage: string;
  technicalMessage?: string;
}

export type UpdateErrorCode =
  | "AUTH_REQUIRED"
  | "FILE_NOT_FOUND"
  | "FILE_NOT_EDITABLE"
  | "VERSION_CONFLICT"
  | "VM_OPERATOR_UNAVAILABLE"
  | "HOST_NOT_READY"
  | "POWERPOINT_UNAVAILABLE"
  | "API_UNSUPPORTED"
  | "JOB_API_FAILED"
  | "TARGET_NOT_FOUND"
  | "TARGET_AMBIGUOUS"
  | "TARGET_NOT_EDITABLE"
  | "ARTIFACT_NOT_FOUND"
  | "ARTIFACT_INVALID"
  | "ARTIFACT_FETCH_FAILED"
  | "OPERATOR_EDIT_FAILED"
  | "OFFICE_SYNC_FAILED"
  | "SAVE_NOT_CONFIRMED"
  | "UPDATE_FAILED";

export interface TargetInspection {
  targetId: string;
  found: boolean;
  editable: boolean;
  type?: TargetType;
  message?: string;
  shapeName?: string;
  source?: TargetSource;
  bound?: boolean;
  tagged?: boolean;
}
