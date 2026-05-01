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

try
{
    var request = JsonSerializer.Deserialize<MailWorkerRequest>(
        File.ReadAllText(requestPath),
        OperatorJson.SerializerOptions)
        ?? throw new InvalidOperationException("Request JSON is empty.");

    using var service = new OutlookMailComService();
    var response = request.Operation switch
    {
        "list-folders" => new MailWorkerResponse
        {
            Folders = await service.ListFoldersAsync(request.ListFolders ?? new MailListFoldersRequest(), CancellationToken.None),
        },
        "search-messages" => new MailWorkerResponse
        {
            Messages = await service.SearchMessagesAsync(
                request.Search ?? throw new InvalidOperationException("search-messages requires Search payload."),
                CancellationToken.None),
        },
        "download-attachments" => new MailWorkerResponse
        {
            Download = await service.DownloadAttachmentsAsync(
                request.Download ?? throw new InvalidOperationException("download-attachments requires Download payload."),
                CancellationToken.None),
        },
        "sync" => new MailWorkerResponse
        {
            Sync = await service.SyncAsync(request.Sync ?? new MailSyncRequest(), CancellationToken.None),
        },
        _ => throw new InvalidOperationException($"Unsupported mail worker operation: {request.Operation}"),
    };

    WriteResponse(responsePath, response);
    return 0;
}
catch (OperatorFailureException ex)
{
    WriteResponse(responsePath, new MailWorkerResponse { Error = ex.Error });
    return 1;
}
catch (Exception ex)
{
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
