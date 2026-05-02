using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Services;

public sealed class OutlookMailComService : IMailService, IDisposable
{
    private const int DefaultInboxFolder = 6;
    private static readonly TimeSpan OutlookLockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleOutlookAge = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan OutlookExitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OutlookOperationTimeout = ReadTimeout(
        "WINDOWS_OPERATOR_OUTLOOK_TIMEOUT_SECONDS",
        TimeSpan.FromSeconds(75));
    private readonly StaComDispatcher _dispatcher = new();
    private readonly MailOptions _policy;

    public OutlookMailComService(MailOptions? policy = null)
    {
        _policy = policy ?? new MailOptions();
    }

    public Task<MailFoldersResult> ListFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => ListFoldersCore(request, _policy), cancellationToken);

    public Task<MailSearchResult> SearchMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => SearchMessagesCore(request, _policy), cancellationToken);

    public Task<MailDownloadResult> DownloadAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => DownloadAttachmentsCore(request, _policy), cancellationToken);

    public Task<MailDownloadResult> GetRunAsync(string runId, CancellationToken cancellationToken) =>
        Task.FromResult(ReadRun(runId));

    public Task<MailStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new MailStatusResult(
            WorkerAvailable: true,
            VisibleOutlookCount: CountOutlookProcesses(visible: true),
            HeadlessOutlookCount: CountOutlookProcesses(visible: false),
            LastWorkerError: null,
            DateTimeOffset.UtcNow));

    public void Dispose() => _dispatcher.Dispose();

    private static MailFoldersResult ListFoldersCore(MailListFoldersRequest request, MailOptions policy)
    {
        var context = new MailOperationContext(policy);
        try
        {
            var rows = ExecuteWithRecovery(context, request.Freshness, session => ReadFolders(session));
            var state = MailSyncState.Load(SyncStatePath());
            state.LastFolderReadUtc = DateTimeOffset.UtcNow;
            state.LastFolderFingerprint = FingerprintFolders(rows);
            state.Save(SyncStatePath());
            context.Actions.Add("folders_read");
            return new MailFoldersResult(
                true,
                rows,
                context.Actions,
                context.Warnings,
                Array.Empty<MailRunError>(),
                context.LastSyncUtc,
                context.Recovered,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new MailFoldersResult(
                false,
                Array.Empty<MailFolderRef>(),
                context.Actions,
                context.Warnings,
                new[] { ToMailRunError(ex) },
                context.LastSyncUtc,
                context.Recovered,
                DateTimeOffset.UtcNow);
        }
    }

    private static MailSearchResult SearchMessagesCore(MailSearchRequest request, MailOptions policy)
    {
        var context = new MailOperationContext(policy);
        try
        {
            var messages = ExecuteWithRecovery(context, request.Freshness, session =>
            {
                var folder = ResolveFolderWithRefresh(session, request.FolderPath, request.Freshness, context);
                var folderPath = FolderPath(folder);
                return SearchFolder(folder, folderPath, request).ToArray();
            });
            context.Actions.Add("messages_searched");
            return new MailSearchResult(
                true,
                messages,
                context.Actions,
                context.Warnings,
                Array.Empty<MailRunError>(),
                context.LastSyncUtc,
                context.Recovered,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new MailSearchResult(
                false,
                Array.Empty<MailMessageRef>(),
                context.Actions,
                context.Warnings,
                new[] { ToMailRunError(ex) },
                context.LastSyncUtc,
                context.Recovered,
                DateTimeOffset.UtcNow);
        }
    }

    private static T ExecuteWithRecovery<T>(MailOperationContext context, string freshness, Func<dynamic, T> operation)
    {
        try
        {
            return ExecuteOnce(context, freshness, operation);
        }
        catch (Exception ex) when (IsRecoverable(ex) && context.Policy.AllowAutomaticSoftRecovery)
        {
            context.Warnings.Add($"soft_recovery_after:{ErrorDetail(ex)}");
            RecoverOutlook(context, includeVisible: false, "soft_recovery");
            context.Recovered = true;
        }

        try
        {
            return ExecuteOnce(context, freshness, operation);
        }
        catch (Exception ex) when (IsRecoverable(ex) && context.Policy.AllowAutomaticRestart)
        {
            context.Warnings.Add($"restart_recovery_after:{ErrorDetail(ex)}");
            RecoverOutlook(context, includeVisible: true, "restart_recovery");
            context.Recovered = true;
        }

        try
        {
            return ExecuteOnce(context, freshness, operation);
        }
        catch (Exception ex) when (IsRecoverable(ex) && context.Policy.AllowAutomaticForceKill)
        {
            context.Warnings.Add($"force_recovery_after:{ErrorDetail(ex)}");
            RecoverOutlook(context, includeVisible: true, "force_recovery");
            context.Recovered = true;
        }

        return ExecuteOnce(context, freshness, operation);
    }

    private static T ExecuteOnce<T>(MailOperationContext context, string freshness, Func<dynamic, T> operation)
    {
        OutlookLease? outlook = null;
        dynamic? session = null;
        try
        {
            outlook = CreateOutlook(context.Policy, context.Actions);
            session = outlook.Application.GetNamespace("MAPI");
            EnsureFresh(session, freshness, context, force: false);
            return operation(session);
        }
        finally
        {
            ReleaseCom(session);
            outlook?.Dispose();
        }
    }

    private static IReadOnlyList<MailFolderRef> ReadFolders(dynamic session)
    {
        var rows = new List<MailFolderRef>();
        for (var i = 1; i <= (int)session.Folders.Count; i++)
        {
            AddFolderRows(session.Folders.Item(i), string.Empty, 0, rows);
        }

        return rows;
    }

    private static dynamic ResolveFolderWithRefresh(dynamic session, string? folderPath, string freshness, MailOperationContext context)
    {
        try
        {
            return ResolveFolder(session, folderPath);
        }
        catch (OperatorFailureException ex) when (
            ex.Error.Code == ErrorCodes.MailFolderNotFound &&
            context.Policy.ForceSyncWhenFolderMissing &&
            IsAutoFreshness(freshness))
        {
            context.Warnings.Add($"folder_missing_before_forced_sync:{folderPath}");
            EnsureFresh(session, MailFreshness.Fresh, context, force: true);
            return ResolveFolder(session, folderPath);
        }
    }

    private static void EnsureFresh(dynamic session, string freshness, MailOperationContext context, bool force)
    {
        var mode = NormalizeFreshness(freshness);
        if (mode == MailFreshness.Cached && !force)
        {
            context.Actions.Add("refresh_skipped:cached");
            context.LastSyncUtc = MailSyncState.Load(SyncStatePath()).LastSyncSuccessUtc;
            return;
        }

        var state = MailSyncState.Load(SyncStatePath());
        context.LastSyncUtc = state.LastSyncSuccessUtc;
        var staleAfter = TimeSpan.FromSeconds(Math.Max(0, context.Policy.SyncFreshnessSeconds));
        var isStale = state.LastSyncSuccessUtc is null ||
            DateTimeOffset.UtcNow - state.LastSyncSuccessUtc.Value >= staleAfter;
        if (!force && mode == MailFreshness.Auto && !isStale)
        {
            context.Actions.Add("refresh_skipped:fresh_cache");
            return;
        }

        state.LastSyncAttemptUtc = DateTimeOffset.UtcNow;
        var waitSeconds = Math.Clamp(context.Policy.SyncWaitSeconds, 0, 75);
        var result = RunSyncSession(session, waitSeconds);
        context.Actions.Add("auto_sync_started");
        context.Actions.AddRange(result.Actions);
        if (!result.Success)
        {
            state.LastError = string.Join("; ", result.Errors);
            state.Save(SyncStatePath());
            throw new OperatorFailureException(
                OperatorErrors.MailUnavailable($"Outlook sync failed before read: {state.LastError}"));
        }

        state.LastSyncSuccessUtc = result.CompletedAtUtc;
        state.LastError = null;
        state.Save(SyncStatePath());
        context.LastSyncUtc = result.CompletedAtUtc;
    }

    private static MailSyncOutcome RunSyncSession(dynamic session, int waitSeconds)
    {
        var actions = new List<string>();
        var errors = new List<string>();
        var startedObjects = new List<object>();
        var started = 0;
        waitSeconds = Math.Clamp(waitSeconds, 0, 75);

        TryLogon(session, actions);
        try
        {
            session.SendAndReceive(false);
            actions.Add("send_and_receive_started");
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        try
        {
            dynamic syncObjects = session.SyncObjects;
            for (var i = 1; i <= (int)syncObjects.Count; i++)
            {
                dynamic syncObject = syncObjects.Item(i);
                syncObject.Start();
                startedObjects.Add(syncObject);
                started++;
            }
        }
        catch (Exception ex)
        {
            errors.Add($"sync_objects_start_failed:{ex.Message}");
        }

        if (waitSeconds > 0)
        {
            Thread.Sleep(TimeSpan.FromSeconds(waitSeconds));
            actions.Add($"waited_seconds:{waitSeconds}");
        }

        foreach (dynamic syncObject in startedObjects)
        {
            try
            {
                syncObject.Stop();
            }
            catch
            {
                // Best effort: some Outlook sync objects finish before Stop.
            }
        }

        if (startedObjects.Count > 0)
        {
            actions.Add($"sync_stop_requested:{startedObjects.Count}");
        }

        return new MailSyncOutcome(
            errors.Count == 0,
            started,
            waitSeconds,
            actions,
            errors,
            DateTimeOffset.UtcNow);
    }

    private static void TryLogon(dynamic session, List<string> actions)
    {
        try
        {
            session.Logon("", "", false, false);
            actions.Add("mapi_logon_checked");
        }
        catch
        {
            // Existing profile may already be logged on; SendAndReceive reports the actionable error if not.
        }
    }

    private static MailDownloadResult DownloadAttachmentsCore(MailDownloadRequest request, MailOptions policy)
    {
        var context = new MailOperationContext(policy);
        var runId = NormalizeRunId(request.RunId);
        var runRoot = Path.Combine(ExchangeRoot(), "runs", runId);
        Directory.CreateDirectory(runRoot);
        var saved = new List<MailSavedAttachment>();
        var skipped = new List<MailSkippedAttachment>();
        var errors = new List<MailRunError>();
        var messagesScanned = 0;
        var messagesMatched = 0;

        try
        {
            ExecuteWithRecovery(context, request.Freshness, session =>
            {
                var candidates = ResolveDownloadCandidates(session, request, context).ToArray();
                messagesScanned = candidates.Length;
                var state = ProcessedState.Load(StatePath());
                foreach (var candidate in candidates)
                {
                    messagesMatched++;
                    SaveAttachments(candidate, request, runRoot, state, saved, skipped, errors);
                }

                if (!request.DryRun)
                {
                    state.Save(StatePath());
                }

                return true;
            });
            context.Actions.Add("attachments_downloaded");
        }
        catch (Exception ex)
        {
            errors.Add(new MailRunError("mail_unavailable", ex.Message, ex.ToString()));
        }

        var result = new MailDownloadResult(
            errors.Count == 0,
            runId,
            runRoot,
            Path.Combine(ExchangeRoot(), "downloads", "mail"),
            messagesScanned,
            messagesMatched,
            saved.Count(item => !item.AlreadyProcessed),
            skipped.Count,
            saved,
            skipped,
            context.Actions,
            context.Warnings,
            errors,
            context.LastSyncUtc,
            context.Recovered,
            DateTimeOffset.UtcNow);
        WriteRunResult(result);
        return result;
    }

    private static IReadOnlyList<DownloadCandidate> ResolveDownloadCandidates(dynamic session, MailDownloadRequest request, MailOperationContext context)
    {
        if (request.MessageIds is { Count: > 0 })
        {
            var selected = new List<DownloadCandidate>();
            foreach (var messageId in request.MessageIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                dynamic item = session.GetItemFromID(messageId);
                selected.Add(new DownloadCandidate(item, request.FolderPath ?? string.Empty, MapMessage(item, request.FolderPath ?? string.Empty, true)));
            }

            return selected;
        }

        var search = new MailSearchRequest
        {
            FolderPath = request.FolderPath,
            SubjectContains = request.SubjectContains,
            ReceivedAfterUtc = request.ReceivedAfterUtc,
            ReceivedBeforeUtc = request.ReceivedBeforeUtc,
            HasAttachments = true,
            IncludeAttachmentDetails = true,
            MaxResults = Math.Clamp(request.MaxMessages, 1, 250),
        };
        var folder = ResolveFolderWithRefresh(session, request.FolderPath, request.Freshness, context);
        var folderPath = FolderPath(folder);
        var candidates = new List<DownloadCandidate>();
        foreach (var message in SearchFolder(folder, folderPath, search))
        {
            dynamic item = session.GetItemFromID(message.MessageId);
            candidates.Add(new DownloadCandidate(item, folderPath, message));
        }

        return candidates;
    }

    private static IEnumerable<MailMessageRef> SearchFolder(dynamic folder, string folderPath, MailSearchRequest request)
    {
        var max = Math.Clamp(request.MaxResults, 1, 250);
        dynamic items = folder.Items;
        items.Sort("[ReceivedTime]", true);
        var found = 0;
        for (var i = 1; i <= (int)items.Count && found < max; i++)
        {
            dynamic item = items.Item(i);
            if (!Matches(item, request))
            {
                continue;
            }

            found++;
            yield return MapMessage(item, folderPath, request.IncludeAttachmentDetails);
        }
    }

    private static bool Matches(dynamic item, MailSearchRequest request)
    {
        var attachmentCount = AttachmentCount(item);
        if (request.HasAttachments is true && attachmentCount <= 0)
        {
            return false;
        }

        if (request.HasAttachments is false && attachmentCount > 0)
        {
            return false;
        }

        var subject = SafeString(() => item.Subject);
        if (!string.IsNullOrWhiteSpace(request.SubjectContains) &&
            !subject.Contains(request.SubjectContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var received = ReceivedTime(item);
        if (request.ReceivedAfterUtc is not null && (received is null || received < request.ReceivedAfterUtc))
        {
            return false;
        }

        if (request.ReceivedBeforeUtc is not null && (received is null || received > request.ReceivedBeforeUtc))
        {
            return false;
        }

        return true;
    }

    private static MailMessageRef MapMessage(dynamic item, string folderPath, bool includeAttachmentDetails)
    {
        var attachmentCount = AttachmentCount(item);
        var attachments = new List<MailAttachmentRef>();
        if (includeAttachmentDetails)
        {
            for (var i = 1; i <= attachmentCount; i++)
            {
                dynamic attachment = item.Attachments.Item(i);
                var fileName = SafeString(() => attachment.FileName);
                attachments.Add(new MailAttachmentRef(
                    i,
                    fileName,
                    Path.GetExtension(fileName),
                    SafeLong(() => attachment.Size)));
            }
        }

        return new MailMessageRef(
            SafeString(() => item.EntryID),
            folderPath,
            SafeString(() => item.Subject),
            ReceivedTime(item),
            attachmentCount,
            attachments);
    }

    private static void SaveAttachments(
        DownloadCandidate candidate,
        MailDownloadRequest request,
        string runRoot,
        ProcessedState state,
        List<MailSavedAttachment> saved,
        List<MailSkippedAttachment> skipped,
        List<MailRunError> errors)
    {
        var attachmentIndexes = request.AttachmentIndexes is { Count: > 0 }
            ? new HashSet<int>(request.AttachmentIndexes)
            : null;
        var message = candidate.Message;
        var date = message.ReceivedTime?.ToString("yyyy-MM-dd") ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var downloadRoot = Path.Combine(ExchangeRoot(), "downloads", "mail", "default", date);
        Directory.CreateDirectory(downloadRoot);

        for (var i = 1; i <= message.AttachmentCount; i++)
        {
            if (attachmentIndexes is not null && !attachmentIndexes.Contains(i))
            {
                continue;
            }

            dynamic attachment = candidate.Item.Attachments.Item(i);
            var fileName = SafeFileName(SafeString(() => attachment.FileName), $"attachment-{i}.bin");
            var size = SafeLong(() => attachment.Size) ?? 0;
            var key = ProcessedState.Key(message.MessageId, fileName, size);
            if (state.TryGetPath(key, out var existingRelative))
            {
                var existingTarget = Path.Combine(ExchangeRoot(), existingRelative);
                if (File.Exists(existingTarget))
                {
                    saved.Add(ToSaved(message, i, fileName, existingTarget, existingRelative, new FileInfo(existingTarget).Length, alreadyProcessed: true));
                    continue;
                }
            }

            if (request.DryRun)
            {
                skipped.Add(new MailSkippedAttachment(message.MessageId, message.FolderPath, message.Subject, message.ReceivedTime, i, fileName, "dry_run"));
                continue;
            }

            try
            {
                var target = UniqueTarget(downloadRoot, fileName);
                var relative = Path.GetRelativePath(ExchangeRoot(), target);
                attachment.SaveAsFile(target);
                var bytes = new FileInfo(target).Length;
                state.Add(key, relative);
                saved.Add(ToSaved(message, i, fileName, target, relative, bytes, alreadyProcessed: false));
            }
            catch (Exception ex)
            {
                errors.Add(new MailRunError("attachment_save_failed", ex.Message, $"{message.MessageId}:{i}"));
            }
        }

        static MailSavedAttachment ToSaved(
            MailMessageRef message,
            int attachmentIndex,
            string fileName,
            string target,
            string relative,
            long bytes,
            bool alreadyProcessed) =>
            new(
                message.MessageId,
                message.FolderPath,
                message.Subject,
                message.ReceivedTime,
                attachmentIndex,
                fileName,
                relative,
                target,
                bytes,
                alreadyProcessed);
    }

    private static void AddFolderRows(dynamic folder, string prefix, int depth, List<MailFolderRef> rows)
    {
        var name = SafeString(() => folder.Name);
        var path = string.IsNullOrWhiteSpace(prefix) ? name : $"{prefix}/{name}";
        var childCount = 0;
        try
        {
            childCount = (int)folder.Folders.Count;
        }
        catch
        {
            childCount = 0;
        }

        rows.Add(new MailFolderRef(depth, path, name, childCount));
        for (var i = 1; i <= childCount; i++)
        {
            AddFolderRows(folder.Folders.Item(i), path, depth + 1, rows);
        }
    }

    private static dynamic ResolveFolder(dynamic session, string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return session.GetDefaultFolder(DefaultInboxFolder);
        }

        var segments = folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return session.GetDefaultFolder(DefaultInboxFolder);
        }

        for (var i = 1; i <= (int)session.Folders.Count; i++)
        {
            dynamic root = session.Folders.Item(i);
            if (!string.Equals(SafeString(() => root.Name), segments[0], StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            dynamic current = root;
            for (var j = 1; j < segments.Length; j++)
            {
                current = FindChild(current, segments[j]);
            }

            return current;
        }

        throw new OperatorFailureException(
            OperatorErrors.MailFolderNotFound(folderPath));
    }

    private static dynamic FindChild(dynamic folder, string name)
    {
        for (var i = 1; i <= (int)folder.Folders.Count; i++)
        {
            dynamic child = folder.Folders.Item(i);
            if (string.Equals(SafeString(() => child.Name), name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        throw new OperatorFailureException(
            OperatorErrors.MailFolderNotFound(name));
    }

    private static string FolderPath(dynamic folder)
    {
        var names = new Stack<string>();
        dynamic? current = folder;
        while (current is not null)
        {
            names.Push(SafeString(() => current.Name));
            try
            {
                current = current.Parent;
                if (current is null || !HasProperty(current, "Name"))
                {
                    break;
                }
            }
            catch
            {
                break;
            }
        }

        return string.Join("/", names);
    }

    private static OutlookLease CreateOutlook(MailOptions policy, List<string> actions)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new OperatorFailureException(
                OperatorErrors.MailUnavailable("Outlook COM requires Windows."));
        }

        var type = Type.GetTypeFromProgID("Outlook.Application", throwOnError: false);
        if (type is null)
        {
            throw new OperatorFailureException(
                OperatorErrors.MailUnavailable("Classic Outlook COM ProgID not registered."));
        }

        var operationLock = OutlookOperationLock.Acquire(OutlookLockTimeout);
        try
        {
            var existingOutlookProcesses = OutlookProcessIds();
            if (existingOutlookProcesses.Count > 0 && !policy.AllowAttachToVisibleOutlook)
            {
                throw new OperatorFailureException(
                    OperatorErrors.MailUnavailable(
                        "Classic Outlook is already open and attach policy is disabled."));
            }

            if (existingOutlookProcesses.Count == 0)
            {
                RemoveOutlookTempFilesIfIdle();
            }

            var application = Activator.CreateInstance(type)
                ?? throw new OperatorFailureException(
                    OperatorErrors.MailUnavailable("Unable to create Outlook.Application COM object."));
            if (existingOutlookProcesses.Count > 0)
            {
                actions.Add("attached_existing_outlook");
            }
            else
            {
                actions.Add("started_outlook");
            }

            return new OutlookLease(
                application,
                existingOutlookProcesses,
                operationLock,
                ownsApplication: existingOutlookProcesses.Count == 0 || !policy.CloseOwnedOutlookOnly);
        }
        catch
        {
            operationLock.Dispose();
            throw;
        }
    }

    private static int AttachmentCount(dynamic item)
    {
        try
        {
            return (int)item.Attachments.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static DateTimeOffset? ReceivedTime(dynamic item)
    {
        try
        {
            DateTime value = item.ReceivedTime;
            return new DateTimeOffset(value);
        }
        catch
        {
            return null;
        }
    }

    private static bool HasProperty(dynamic value, string propertyName)
    {
        try
        {
            _ = value.GetType().InvokeMember(propertyName, System.Reflection.BindingFlags.GetProperty, null, value, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeString(Func<dynamic> getter)
    {
        try
        {
            return Convert.ToString(getter(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static long? SafeLong(Func<dynamic> getter)
    {
        try
        {
            return Convert.ToInt64(getter(), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static string SafeFileName(string raw, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? fallback : raw;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Length > 180 ? value[..180] : value;
    }

    private static string UniqueTarget(string directory, string fileName)
    {
        var target = Path.Combine(directory, fileName);
        if (!File.Exists(target))
        {
            return target;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; i < 10_000; i++)
        {
            target = Path.Combine(directory, $"{stem}-{i}{extension}");
            if (!File.Exists(target))
            {
                return target;
            }
        }

        return Path.Combine(directory, $"{stem}-{Guid.NewGuid():N}{extension}");
    }

    private static string NormalizeRunId(string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return SafeFileName(raw, "mail-download");
        }

        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return $"mail-download-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string ExchangeRoot() =>
        Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_EXCHANGE_ROOT")
        ?? @"Z:\operator-exchange";

    private static string StatePath()
    {
        var root = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsOperator");
        }

        return Path.Combine(root, "run", "mail-download-state.json");
    }

    private static void WriteRunResult(MailDownloadResult result)
    {
        Directory.CreateDirectory(result.RunRoot);
        File.WriteAllText(
            Path.Combine(result.RunRoot, "result.json"),
            JsonSerializer.Serialize(result, OperatorJson.SerializerOptions));
    }

    private static MailDownloadResult ReadRun(string runId)
    {
        var path = Path.Combine(ExchangeRoot(), "runs", SafeFileName(runId, "missing"), "result.json");
        if (!File.Exists(path))
        {
            throw new OperatorFailureException(
                OperatorErrors.MailRunNotFound(runId));
        }

        return JsonSerializer.Deserialize<MailDownloadResult>(File.ReadAllText(path), OperatorJson.SerializerOptions)
            ?? throw new OperatorFailureException(
                OperatorErrors.MailRunNotFound(runId));
    }

    private static OperatorFailureException MailUnavailable(Exception ex) =>
        ex is OperatorFailureException operatorFailure
            ? operatorFailure
            : new OperatorFailureException(OperatorErrors.MailUnavailable(ex.Message));

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static void CollectComGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private static TimeSpan ReadTimeout(string name, TimeSpan fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(raw, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return fallback;
    }

    private static HashSet<int> OutlookProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("OUTLOOK"))
        {
            using (process)
            {
                ids.Add(process.Id);
            }
        }

        return ids;
    }

    private static bool HasMainWindow(Process process)
    {
        try
        {
            process.Refresh();
            return process.MainWindowHandle != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static int CountOutlookProcesses(bool visible)
    {
        var count = 0;
        foreach (var process in Process.GetProcessesByName("OUTLOOK"))
        {
            using (process)
            {
                if (HasMainWindow(process) == visible)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void WaitForOutlookExitOrWindow(HashSet<int> preservedProcessIds, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var hasHeadless = Process.GetProcessesByName("OUTLOOK")
                .Any(process =>
                {
                    using (process)
                    {
                        return !preservedProcessIds.Contains(process.Id) && !HasMainWindow(process);
                    }
                });
            if (!hasHeadless)
            {
                return;
            }

            Thread.Sleep(250);
        }
    }

    private static void StopHeadlessOutlookProcesses(HashSet<int> preservedProcessIds, TimeSpan minimumAge)
    {
        foreach (var process in Process.GetProcessesByName("OUTLOOK"))
        {
            using (process)
            {
                if (preservedProcessIds.Contains(process.Id) || HasMainWindow(process) || !IsAtLeast(process, minimumAge))
                {
                    continue;
                }

                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch
                {
                    // Best effort: Outlook may exit between process scan and kill.
                }
            }
        }
    }

    private static void RemoveOutlookTempFilesIfIdle()
    {
        var outlookProcesses = Process.GetProcessesByName("OUTLOOK");
        try
        {
            if (outlookProcesses.Length > 0)
            {
                return;
            }
        }
        finally
        {
            foreach (var process in outlookProcesses)
            {
                process.Dispose();
            }
        }

        var outlookDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "Outlook");
        if (!Directory.Exists(outlookDataPath))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(outlookDataPath, "~*.tmp"))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best effort: Outlook may restart while cleanup is scanning.
            }
        }
    }

    private static bool IsAtLeast(Process process, TimeSpan age)
    {
        if (age <= TimeSpan.Zero)
        {
            return true;
        }

        try
        {
            return DateTime.Now - process.StartTime >= age;
        }
        catch
        {
            return true;
        }
    }

    private static void RecoverOutlook(MailOperationContext context, bool includeVisible, string action)
    {
        context.Actions.Add(action);
        StopOutlookProcesses(includeVisible, context.Actions, context.Warnings);
        RemoveOutlookTempFilesIfIdle();
    }

    private static void StopOutlookProcesses(bool includeVisible, List<string> actions, List<string> warnings)
    {
        foreach (var process in Process.GetProcessesByName("OUTLOOK"))
        {
            using (process)
            {
                var visible = HasMainWindow(process);
                if (visible && !includeVisible)
                {
                    continue;
                }

                try
                {
                    if (visible)
                    {
                        process.CloseMainWindow();
                        if (process.WaitForExit(5000))
                        {
                            actions.Add($"outlook_closed:{process.Id}");
                            continue;
                        }
                    }

                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                    actions.Add($"outlook_killed:{process.Id}");
                }
                catch (Exception ex)
                {
                    warnings.Add($"outlook_stop_failed:{process.Id}:{ex.Message}");
                }
            }
        }
    }

    private static bool IsRecoverable(Exception ex) =>
        ex is not OperationCanceledException;

    private static string ErrorDetail(Exception ex) =>
        ex is OperatorFailureException failure
            ? failure.Error.Details?.GetValueOrDefault("detail") ?? failure.Error.Message
            : ex.Message;

    private static MailRunError ToMailRunError(Exception ex) =>
        ex is OperatorFailureException failure
            ? new MailRunError(failure.Error.Code, ErrorDetail(ex), failure.Error.Message)
            : new MailRunError("mail_unavailable", ex.Message, ex.ToString());

    private static string NormalizeFreshness(string? raw)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? MailFreshness.Auto : raw.Trim().ToLowerInvariant();
        return value is MailFreshness.Auto or MailFreshness.Cached or MailFreshness.Fresh
            ? value
            : throw new OperatorFailureException(
                OperatorErrors.MailUnavailable($"Unsupported mail freshness: {raw}"));
    }

    private static bool IsAutoFreshness(string? raw) =>
        NormalizeFreshness(raw) == MailFreshness.Auto;

    private static string FingerprintFolders(IReadOnlyList<MailFolderRef> folders)
    {
        var text = string.Join("\n", folders.Select(folder => $"{folder.Depth}|{folder.Path}|{folder.ChildCount}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string SyncStatePath()
    {
        var root = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsOperator");
        }

        return Path.Combine(root, "run", "mail-sync-state.json");
    }

    private sealed record DownloadCandidate(dynamic Item, string FolderPath, MailMessageRef Message);

    private sealed record MailSyncOutcome(
        bool Success,
        int SyncObjectsStarted,
        int WaitSeconds,
        IReadOnlyList<string> Actions,
        IReadOnlyList<string> Errors,
        DateTimeOffset CompletedAtUtc);

    private sealed class MailOperationContext
    {
        public MailOperationContext(MailOptions policy)
        {
            Policy = policy;
        }

        public MailOptions Policy { get; }

        public List<string> Actions { get; } = new();

        public List<string> Warnings { get; } = new();

        public DateTimeOffset? LastSyncUtc { get; set; }

        public bool Recovered { get; set; }
    }

    private sealed class MailSyncState
    {
        public DateTimeOffset? LastSyncAttemptUtc { get; set; }

        public DateTimeOffset? LastSyncSuccessUtc { get; set; }

        public DateTimeOffset? LastFolderReadUtc { get; set; }

        public string? LastFolderFingerprint { get; set; }

        public string? LastError { get; set; }

        public static MailSyncState Load(string path)
        {
            if (!File.Exists(path))
            {
                return new MailSyncState();
            }

            return JsonSerializer.Deserialize<MailSyncState>(File.ReadAllText(path), OperatorJson.SerializerOptions)
                ?? new MailSyncState();
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, JsonSerializer.Serialize(this, OperatorJson.SerializerOptions));
        }
    }

    private sealed class OutlookLease : IDisposable
    {
        private readonly HashSet<int> _preservedOutlookProcesses;
        private readonly OutlookOperationLock _operationLock;
        private readonly System.Threading.Timer _watchdog;
        private readonly bool _ownsApplication;
        private bool _disposed;

        public OutlookLease(dynamic application, HashSet<int> preservedOutlookProcesses, OutlookOperationLock operationLock, bool ownsApplication)
        {
            Application = application;
            _preservedOutlookProcesses = preservedOutlookProcesses;
            _operationLock = operationLock;
            _ownsApplication = ownsApplication;
            _watchdog = new System.Threading.Timer(
                _ => StopHeadlessOutlookProcesses(_preservedOutlookProcesses, TimeSpan.Zero),
                state: null,
                dueTime: OutlookOperationTimeout,
                period: Timeout.InfiniteTimeSpan);
        }

        public dynamic Application { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watchdog.Dispose();
            try
            {
                if (_ownsApplication)
                {
                    Application.Quit();
                }
            }
            catch
            {
                // Best effort: Outlook may refuse Quit while initializing or showing a modal.
            }
            finally
            {
                ReleaseCom(Application);
                CollectComGarbage();
                if (_ownsApplication)
                {
                    WaitForOutlookExitOrWindow(_preservedOutlookProcesses, OutlookExitTimeout);
                    StopHeadlessOutlookProcesses(_preservedOutlookProcesses, TimeSpan.Zero);
                }

                RemoveOutlookTempFilesIfIdle();
                _operationLock.Dispose();
            }
        }
    }

    private sealed class OutlookOperationLock : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _disposed;

        private OutlookOperationLock(Mutex mutex)
        {
            _mutex = mutex;
        }

        public static OutlookOperationLock Acquire(TimeSpan timeout)
        {
            var mutex = new Mutex(initiallyOwned: false, @"Local\WindowsOperator.OutlookMail");
            try
            {
                if (!mutex.WaitOne(timeout))
                {
                    mutex.Dispose();
                    throw new OperatorFailureException(
                        OperatorErrors.MailUnavailable("Another Outlook mail automation operation is already running."));
                }
            }
            catch (AbandonedMutexException)
            {
                return new OutlookOperationLock(mutex);
            }

            return new OutlookOperationLock(mutex);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }

    private sealed class ProcessedState
    {
        public Dictionary<string, string> Processed { get; init; } = new(StringComparer.Ordinal);

        public static ProcessedState Load(string path)
        {
            if (!File.Exists(path))
            {
                return new ProcessedState();
            }

            return JsonSerializer.Deserialize<ProcessedState>(File.ReadAllText(path), OperatorJson.SerializerOptions)
                ?? new ProcessedState();
        }

        public static string Key(string messageId, string fileName, long size) =>
            $"{messageId}|{fileName}|{size}";

        public bool TryGetPath(string key, out string relativePath) =>
            Processed.TryGetValue(key, out relativePath!);

        public void Add(string key, string relativePath) => Processed[key] = relativePath;

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, OperatorJson.SerializerOptions));
        }
    }
}
