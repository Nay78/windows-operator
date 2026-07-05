export const WINDOWS_OPERATOR_ADDIN_CHANNEL = "windows-operator.powerpoint-addin";
export const RUN_PENDING_JOB_COMMAND = "runPendingJob";

export type AddInCommandRequest = {
  channel: typeof WINDOWS_OPERATOR_ADDIN_CHANNEL;
  kind: "command";
  command: typeof RUN_PENDING_JOB_COMMAND;
  requestId: string;
};

export type AddInCommandAck = {
  channel: typeof WINDOWS_OPERATOR_ADDIN_CHANNEL;
  kind: "ack";
  command: typeof RUN_PENDING_JOB_COMMAND;
  requestId: string;
  accepted: boolean;
  error?: string;
};

type MessageSourceLike = {
  postMessage(message: unknown, targetOrigin: string): void;
};

type AddInMessageEvent = {
  data: unknown;
  origin?: string;
  source: MessageSourceLike | null;
};

type MessageListener = (event: AddInMessageEvent) => void;

export interface MessageTargetLike {
  addEventListener(type: "message", listener: MessageListener): void;
  removeEventListener(type: "message", listener: MessageListener): void;
}

export function registerRunPendingJobCommandHandler(
  target: MessageTargetLike,
  runPendingJob: () => Promise<void>,
): () => void {
  const listener: MessageListener = event => {
    if (!isRunPendingJobCommand(event.data)) {
      return;
    }

    const ack: AddInCommandAck = {
      channel: WINDOWS_OPERATOR_ADDIN_CHANNEL,
      kind: "ack",
      command: RUN_PENDING_JOB_COMMAND,
      requestId: event.data.requestId,
      accepted: true,
    };

    try {
      void runPendingJob();
      event.source?.postMessage(ack, event.origin ?? "*");
    } catch (error) {
      event.source?.postMessage(
        {
          ...ack,
          accepted: false,
          error: error instanceof Error ? error.message : "run_pending_job_failed",
        } satisfies AddInCommandAck,
        event.origin ?? "*",
      );
    }
  };

  target.addEventListener("message", listener);
  return () => target.removeEventListener("message", listener);
}

function isRunPendingJobCommand(data: unknown): data is AddInCommandRequest {
  return isRecord(data) &&
    data.channel === WINDOWS_OPERATOR_ADDIN_CHANNEL &&
    data.kind === "command" &&
    data.command === RUN_PENDING_JOB_COMMAND &&
    typeof data.requestId === "string" &&
    data.requestId.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
