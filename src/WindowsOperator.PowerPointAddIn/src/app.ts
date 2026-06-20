import "./styles.css";
import { HttpArtifactResolver } from "./artifacts/httpArtifactResolver";
import { readConfig } from "./config";
import { toUpdateError } from "./domain/errors";
import { HttpJobClient } from "./jobs/httpJobClient";
import { MockJobClient } from "./jobs/mockJobClient";
import { OfficeCurrentDocumentProvider } from "./office/currentDocument";
import { OfficePresentationAdapter } from "./office/presentationAdapter";
import type { UpdateJobClient } from "./ports";
import { OfficeTemplateBootstrapper } from "./template/officeTemplateBootstrapper";
import { resultFromFailure, UpdateEngine } from "./update/updateEngine";

const app = document.querySelector<HTMLElement>("#app");

if (!app) {
  throw new Error("App root not found.");
}

const appRoot = app;

const state = {
  running: false,
  log: [] as string[],
};

void boot();

async function boot(): Promise<void> {
  render();
  await waitForOffice();
  log("ready");
  render();
}

function render(): void {
  appRoot.replaceChildren();

  const shell = document.createElement("section");
  shell.className = "shell";

  const header = document.createElement("header");
  const title = document.createElement("h1");
  title.textContent = "Windows Operator PowerPoint";
  const subtitle = document.createElement("p");
  subtitle.textContent = "Run a pending presentation update job.";
  header.append(title, subtitle);

  const controls = document.createElement("div");
  controls.className = "controls";
  controls.append(
    actionButton("Prepare Template", () => void prepareTemplate(), state.running),
    actionButton(state.running ? "Running..." : "Run Pending Job", () => void runJob(), state.running),
  );

  const logList = document.createElement("ol");
  logList.className = "log";
  for (const line of state.log) {
    const item = document.createElement("li");
    item.textContent = line;
    logList.append(item);
  }

  shell.append(header, controls, logList);
  appRoot.append(shell);
}

async function runJob(): Promise<void> {
  state.running = true;
  render();

  const config = readConfig();
  const jobClient = createJobClient(config.useMockJob, config.jobApiBaseUrl);
  let currentJob = null as Awaited<ReturnType<UpdateJobClient["claimNextJob"]>>;
  const currentDocument = new OfficeCurrentDocumentProvider();
  const engine = new UpdateEngine(
    new OfficePresentationAdapter(),
    new HttpArtifactResolver(),
    currentDocument,
  );

  try {
    const job = await jobClient.claimNextJob(currentDocument.getUrl());
    currentJob = job;
    if (!job) {
      log("no pending job");
      return;
    }

    log(`claimed ${job.jobId}`);
    const result = await engine.apply(job);
    await jobClient.complete(result);
    log(`${result.status} ${job.jobId}`);
  } catch (error) {
    log(toUpdateError(error, "UPDATE_FAILED").operatorMessage);
    if (currentJob) {
      const result = resultFromFailure(currentJob, error, "UPDATE_FAILED");
      await jobClient.fail(
        currentJob.jobId,
        result.targets[0]?.error ?? toUpdateError(error, "UPDATE_FAILED"),
      );
    }
  } finally {
    state.running = false;
    render();
  }
}

async function prepareTemplate(): Promise<void> {
  state.running = true;
  render();

  try {
    const bootstrapper = new OfficeTemplateBootstrapper();
    const result = await bootstrapper.ensureMockTargets();
    log(`template ready created=${result.created.length} existing=${result.existing.length}`);
  } catch (error) {
    log(toUpdateError(error, "OFFICE_SYNC_FAILED").operatorMessage);
  } finally {
    state.running = false;
    render();
  }
}

function createJobClient(useMockJob: boolean, baseUrl?: string): UpdateJobClient {
  if (useMockJob) {
    return new MockJobClient(log);
  }

  return new HttpJobClient(baseUrl ?? "");
}

async function waitForOffice(): Promise<void> {
  if (typeof Office === "undefined") {
    log("Office.js not detected");
    return;
  }

  await Office.onReady();
}

function log(line: string): void {
  state.log = [`${new Date().toLocaleTimeString()} ${line}`, ...state.log].slice(0, 12);
}

function actionButton(label: string, onClick: () => void, disabled = false): HTMLButtonElement {
  const button = document.createElement("button");
  button.type = "button";
  button.textContent = label;
  button.disabled = disabled;
  button.addEventListener("click", onClick);
  return button;
}
