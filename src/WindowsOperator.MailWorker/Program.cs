using System.Text.Json;
using WindowsOperator.Agent.Services;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: WindowsOperator.MailWorker <request.json> <response.json>");
    return 2;
}

var requestPath = args[0];
var responsePath = args[1];
Directory.CreateDirectory(Path.GetDirectoryName(responsePath) ?? ".");
var progressPath = Path.Combine(Path.GetDirectoryName(responsePath) ?? ".", "progress.log");

void Trace(string stage)
{
    try
    {
        File.AppendAllText(
            progressPath,
            $"{DateTimeOffset.UtcNow:o}\t{stage}{Environment.NewLine}");
    }
    catch
    {
        // Diagnostics must not break mail automation.
    }
}

try
{
    Trace("worker_start");
    var request = JsonSerializer.Deserialize<MailWorkerRequest>(
        File.ReadAllText(requestPath),
        OperatorJson.SerializerOptions)
        ?? throw new InvalidOperationException("Request JSON is empty.");
    Trace($"request_loaded:{request.Operation}");

    using var service = new OutlookMailComService(request.Policy, Trace);
    Trace("service_created");
    MailWorkerResponse response;
    switch (request.Operation)
    {
        case "list-folders":
            Trace("operation_start:list-folders");
            response = new MailWorkerResponse
            {
                Folders = await service.ListFoldersAsync(request.ListFolders ?? new MailListFoldersRequest(), CancellationToken.None),
            };
            break;
        case "search-messages":
            Trace("operation_start:search-messages");
            response = new MailWorkerResponse
            {
                Messages = await service.SearchMessagesAsync(
                    request.Search ?? throw new InvalidOperationException("search-messages requires Search payload."),
                    CancellationToken.None),
            };
            break;
        case "download-attachments":
            Trace("operation_start:download-attachments");
            response = new MailWorkerResponse
            {
                Download = await service.DownloadAttachmentsAsync(
                    request.Download ?? throw new InvalidOperationException("download-attachments requires Download payload."),
                    CancellationToken.None),
            };
            break;
        default:
            throw new InvalidOperationException($"Unsupported mail worker operation: {request.Operation}");
    }

    Trace("response_write");
    WriteResponse(responsePath, response);
    Trace("worker_success");
    return 0;
}
catch (OperatorFailureException ex)
{
    Trace($"operator_failure:{ex.Error.Code}:{ex.Error.Message}");
    WriteResponse(responsePath, new MailWorkerResponse { Error = ex.Error });
    return 1;
}
catch (Exception ex)
{
    Trace($"unhandled:{ex.GetType().FullName}:{ex.Message}");
    WriteResponse(responsePath, new MailWorkerResponse
    {
        Error = OperatorErrors.MailUnavailable(ex.Message),
    });
    return 1;
}

static void WriteResponse(string path, MailWorkerResponse response)
{
    File.WriteAllText(path, JsonSerializer.Serialize(response, OperatorJson.SerializerOptions));
}
