using Microsoft.AspNetCore.Http.HttpResults;
using TypedResults = Microsoft.AspNetCore.Http.TypedResults;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Host.Api;

public static class HostOperatorHttp
{
    public static async Task<Results<Ok<T>, JsonHttpResult<OperatorError>>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return TypedResults.Ok(await action());
        }
        catch (OperatorFailureException failure)
        {
            return Error(failure);
        }
    }

    public static JsonHttpResult<OperatorError> Error(OperatorFailureException failure) =>
        TypedResults.Json(WithCorrelationId(failure.Error), statusCode: MapStatusCode(failure.Error.Code));

    public static int MapStatusCode(string errorCode) =>
        errorCode switch
        {
            ErrorCodes.InvalidRequest => StatusCodes.Status400BadRequest,
            ErrorCodes.InternalError => StatusCodes.Status500InternalServerError,
            ErrorCodes.WindowNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.LockedDesktop => StatusCodes.Status423Locked,
            ErrorCodes.UipiBlocked => StatusCodes.Status409Conflict,
            ErrorCodes.ElevatedTarget => StatusCodes.Status409Conflict,
            ErrorCodes.BlankCapture => StatusCodes.Status409Conflict,
            ErrorCodes.MinimizedRdp => StatusCodes.Status409Conflict,
            ErrorCodes.UnsupportedControl => StatusCodes.Status422UnprocessableEntity,
            ErrorCodes.AuthUnavailable => StatusCodes.Status423Locked,
            ErrorCodes.AuthRunNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.BrowserSessionNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.WorkbenchSessionNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.PowerPointValidationFailed => StatusCodes.Status422UnprocessableEntity,
            ErrorCodes.PowerPointSessionNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.PowerPointJobNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.ArtifactNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.PowerPointUnavailable => StatusCodes.Status423Locked,
            ErrorCodes.DevAutomationDisabled => StatusCodes.Status422UnprocessableEntity,
            ErrorCodes.DevRawJsDisabled => StatusCodes.Status422UnprocessableEntity,
            ErrorCodes.DevAutomationValidationFailed => StatusCodes.Status422UnprocessableEntity,
            ErrorCodes.MailFolderNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.MailRunNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.MailUnavailable => StatusCodes.Status423Locked,
            ErrorCodes.PowerAutomateMcpUnavailable => StatusCodes.Status423Locked,
            ErrorCodes.PowerAutomateMcpValidationFailed => StatusCodes.Status422UnprocessableEntity,
            ErrorCodes.OpenApiNamespaceNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.OpenApiSurfaceInvalid => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError,
        };

    private static OperatorError WithCorrelationId(OperatorError error) =>
        string.IsNullOrWhiteSpace(error.CorrelationId)
            ? error with { CorrelationId = Guid.NewGuid().ToString("n") }
            : error;
}
