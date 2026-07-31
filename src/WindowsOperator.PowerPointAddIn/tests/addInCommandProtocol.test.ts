import { describe, expect, it, vi } from "vitest";
import {
  type MessageTargetLike,
  registerRunPendingJobCommandHandler,
  RUN_PENDING_JOB_COMMAND,
  WINDOWS_OPERATOR_ADDIN_CHANNEL,
} from "../src/addInCommandProtocol";

describe("addInCommandProtocol", () => {
  it("invokes the registered runJob path and acknowledges the command", async () => {
    const bus = new FakeMessageTarget();
    const runJob = vi.fn(async () => {});
    const source = new FakeMessageSource();
    registerRunPendingJobCommandHandler(bus, runJob);

    bus.dispatch({
      data: {
        channel: WINDOWS_OPERATOR_ADDIN_CHANNEL,
        kind: "command",
        command: RUN_PENDING_JOB_COMMAND,
        requestId: "req-1",
      },
      origin: "https://powerpoint.office.com",
      source,
    });

    expect(runJob).toHaveBeenCalledOnce();
    expect(source.messages).toEqual([
      {
        message: {
          channel: WINDOWS_OPERATOR_ADDIN_CHANNEL,
          kind: "ack",
          command: RUN_PENDING_JOB_COMMAND,
          requestId: "req-1",
          accepted: true,
        },
        targetOrigin: "https://powerpoint.office.com",
      },
    ]);
  });

  it("ignores unrelated messages", () => {
    const bus = new FakeMessageTarget();
    const runJob = vi.fn(async () => {});
    const source = new FakeMessageSource();
    registerRunPendingJobCommandHandler(bus, runJob);

    bus.dispatch({
      data: {
        channel: WINDOWS_OPERATOR_ADDIN_CHANNEL,
        kind: "command",
        command: "cleanupTemplate",
        requestId: "req-2",
      },
      origin: "https://powerpoint.office.com",
      source,
    });

    expect(runJob).not.toHaveBeenCalled();
    expect(source.messages).toEqual([]);
  });
});

class FakeMessageTarget implements MessageTargetLike {
  private listener: Parameters<MessageTargetLike["addEventListener"]>[1] | null = null;

  addEventListener(_type: "message", listener: Parameters<MessageTargetLike["addEventListener"]>[1]): void {
    this.listener = listener;
  }

  removeEventListener(_type: "message", listener: Parameters<MessageTargetLike["removeEventListener"]>[1]): void {
    if (this.listener === listener) {
      this.listener = null;
    }
  }

  dispatch(event: Parameters<NonNullable<typeof this.listener>>[0]): void {
    this.listener?.(event);
  }
}

class FakeMessageSource {
  messages: Array<{ message: unknown; targetOrigin: string }> = [];

  postMessage(message: unknown, targetOrigin: string): void {
    this.messages.push({ message, targetOrigin });
  }
}
