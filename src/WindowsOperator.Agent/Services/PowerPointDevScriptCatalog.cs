namespace WindowsOperator.Agent.Services;

internal interface IPowerPointDevScriptCatalog
{
    PowerPointDevScriptDefinition? Find(string scriptId);
}

internal sealed class PowerPointDevScriptCatalog : IPowerPointDevScriptCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, PowerPointDevScriptDefinition>> Scripts = new(() =>
        new Dictionary<string, PowerPointDevScriptDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["ppt.dom.snapshot"] = new(
                "ppt.dom.snapshot",
                DomSnapshot!,
                MutatesDeck: false,
                TimeoutCapSeconds: 10,
                Target: "powerpoint-page"),
            ["ppt.ribbon.commands"] = new(
                "ppt.ribbon.commands",
                RibbonCommands!,
                MutatesDeck: false,
                TimeoutCapSeconds: 10,
                Target: "powerpoint-page"),
            ["ppt.addin.frames"] = new(
                "ppt.addin.frames",
                AddInFrames!,
                MutatesDeck: false,
                TimeoutCapSeconds: 10,
                Target: "powerpoint-page"),
            ["ppt.office.context"] = new(
                "ppt.office.context",
                OfficeContext!,
                MutatesDeck: false,
                TimeoutCapSeconds: 10,
                Target: "powerpoint-page"),
            ["ppt.save.state"] = new(
                "ppt.save.state",
                SaveState!,
                MutatesDeck: false,
                TimeoutCapSeconds: 10,
                Target: "powerpoint-page"),
        });

    public PowerPointDevScriptDefinition? Find(string scriptId) =>
        Scripts.Value.TryGetValue(scriptId, out var script) ? script : null;

    private const string SharedHelpers = """
const normalize = value => (value || "").replace(/\s+/g, " ").trim();
const isVisible = element => {
  if (!element || !element.getBoundingClientRect) {
    return false;
  }
  const style = window.getComputedStyle(element);
  if (!style || style.visibility === "hidden" || style.display === "none") {
    return false;
  }
  const rect = element.getBoundingClientRect();
  return rect.width > 0 && rect.height > 0;
};
const rectOf = element => {
  if (!element || !element.getBoundingClientRect) {
    return null;
  }
  const rect = element.getBoundingClientRect();
  return {
    x: Math.round(rect.x),
    y: Math.round(rect.y),
    width: Math.round(rect.width),
    height: Math.round(rect.height)
  };
};
const textOf = element => normalize(
  element?.innerText ||
  element?.textContent ||
  element?.value ||
  element?.placeholder ||
  element?.getAttribute?.("aria-label") ||
  element?.getAttribute?.("title") ||
  ""
);
const labelOf = element => {
  const values = [];
  if (element && element.labels) {
    for (const label of element.labels) {
      values.push(label.innerText || label.textContent || "");
    }
  }
  if (element && element.id) {
    for (const label of document.querySelectorAll(`label[for="${CSS.escape(element.id)}"]`)) {
      values.push(label.innerText || label.textContent || "");
    }
  }
  const parent = element && element.closest ? element.closest("label") : null;
  if (parent) {
    values.push(parent.innerText || parent.textContent || "");
  }
  return normalize(values.join(" "));
};
const frameInfo = frame => {
  let accessible = false;
  let childTitle = null;
  let childUrl = null;
  try {
    accessible = !!frame.contentWindow?.document;
    childTitle = frame.contentWindow.document.title || null;
    childUrl = frame.contentWindow.location.href || null;
  } catch {
    accessible = false;
  }
  return {
    id: frame.id || null,
    name: frame.name || null,
    title: frame.title || frame.getAttribute("aria-label") || null,
    src: frame.src || frame.getAttribute("src") || null,
    visible: isVisible(frame),
    rect: rectOf(frame),
    accessible,
    childTitle,
    childUrl
  };
};
""";

    private static readonly string DomSnapshot = $$"""
(() => {
{{SharedHelpers}}
  const elements = Array.from(document.querySelectorAll("button,a,input,textarea,select,[role='button'],[role='menuitem'],[aria-label]"))
    .filter(isVisible)
    .slice(0, 100)
    .map(element => ({
      tagName: (element.tagName || "").toLowerCase(),
      role: element.getAttribute?.("role") || null,
      type: element.getAttribute?.("type") || null,
      text: textOf(element),
      label: labelOf(element) || null,
      ariaLabel: element.getAttribute?.("aria-label") || null,
      title: element.getAttribute?.("title") || null,
      id: element.id || null,
      name: element.getAttribute?.("name") || null,
      rect: rectOf(element)
    }));
  return JSON.stringify({
    kind: "ppt.dom.snapshot",
    title: document.title || "",
    url: window.location.href || "",
    bodyText: normalize(document.body ? document.body.innerText || "" : "").slice(0, 20000),
    viewport: { width: window.innerWidth, height: window.innerHeight },
    frames: Array.from(document.querySelectorAll("iframe")).map(frameInfo),
    elements
  });
})()
""";

    private static readonly string RibbonCommands = $$"""
(() => {
{{SharedHelpers}}
  const commandSelector = "button,[role='button'],[role='menuitem'],a,[aria-label],[data-automation-id],[data-testid]";
  const commands = Array.from(document.querySelectorAll(commandSelector))
    .filter(isVisible)
    .map(element => ({
      tagName: (element.tagName || "").toLowerCase(),
      role: element.getAttribute?.("role") || null,
      text: textOf(element),
      ariaLabel: element.getAttribute?.("aria-label") || null,
      title: element.getAttribute?.("title") || null,
      automationId: element.getAttribute?.("data-automation-id") || null,
      testId: element.getAttribute?.("data-testid") || null,
      id: element.id || null,
      rect: rectOf(element)
    }))
    .filter(command => command.text || command.ariaLabel || command.title || command.automationId || command.testId)
    .slice(0, 160);
  const lower = value => (value || "").toLowerCase();
  const needles = ["home", "insert", "add-ins", "addins", "run update", "advanced", "upload", "my add-ins", "file", "saved", "saving"];
  const candidates = commands.filter(command => needles.some(needle =>
    lower(command.text).includes(needle) ||
    lower(command.ariaLabel).includes(needle) ||
    lower(command.title).includes(needle) ||
    lower(command.automationId).includes(needle) ||
    lower(command.testId).includes(needle)));
  return JSON.stringify({
    kind: "ppt.ribbon.commands",
    title: document.title || "",
    url: window.location.href || "",
    commandCount: commands.length,
    candidates,
    commands
  });
})()
""";

    private static readonly string AddInFrames = $$"""
(() => {
{{SharedHelpers}}
  const frames = Array.from(document.querySelectorAll("iframe")).map(frameInfo);
  const addInCandidates = frames.filter(frame => {
    const value = `${frame.title || ""} ${frame.src || ""} ${frame.childTitle || ""} ${frame.childUrl || ""}`.toLowerCase();
    return value.includes("addin") || value.includes("add-in") || value.includes("taskpane") || value.includes("localhost") || value.includes("office");
  });
  return JSON.stringify({
    kind: "ppt.addin.frames",
    title: document.title || "",
    url: window.location.href || "",
    frameCount: frames.length,
    addInCandidates,
    frames
  });
})()
""";

    private static readonly string OfficeContext = $$"""
(() => {
{{SharedHelpers}}
  const office = typeof Office === "undefined" ? null : Office;
  const context = office && office.context ? office.context : null;
  return JSON.stringify({
    kind: "ppt.office.context",
    title: document.title || "",
    url: window.location.href || "",
    officeAvailable: !!office,
    host: context?.host || null,
    platform: context?.platform || null,
    diagnostics: context?.diagnostics || null,
    requirementsAvailable: !!office?.context?.requirements,
    frames: Array.from(document.querySelectorAll("iframe")).map(frameInfo)
  });
})()
""";

    private static readonly string SaveState = $$"""
(() => {
{{SharedHelpers}}
  const bodyText = normalize(document.body ? document.body.innerText || "" : "");
  const lower = bodyText.toLowerCase();
  const indicators = [
    "saved",
    "saving",
    "all changes saved",
    "saved to",
    "last saved",
    "syncing",
    "uploading"
  ].filter(value => lower.includes(value));
  const controls = Array.from(document.querySelectorAll("button,[role='button'],[aria-label],span,div"))
    .filter(isVisible)
    .map(element => ({
      text: textOf(element),
      ariaLabel: element.getAttribute?.("aria-label") || null,
      title: element.getAttribute?.("title") || null,
      rect: rectOf(element)
    }))
    .filter(item => {
      const value = `${item.text || ""} ${item.ariaLabel || ""} ${item.title || ""}`.toLowerCase();
      return value.includes("saved") || value.includes("saving") || value.includes("sync");
    })
    .slice(0, 40);
  return JSON.stringify({
    kind: "ppt.save.state",
    title: document.title || "",
    url: window.location.href || "",
    indicators,
    controls
  });
})()
""";
}

internal sealed record PowerPointDevScriptDefinition(
    string ScriptId,
    string Expression,
    bool MutatesDeck,
    int TimeoutCapSeconds,
    string Target);
