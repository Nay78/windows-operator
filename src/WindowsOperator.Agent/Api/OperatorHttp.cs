using Microsoft.AspNetCore.Http.HttpResults;
using TypedResults = Microsoft.AspNetCore.Http.TypedResults;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Agent.Api;

public static class OperatorHttp
{
    public static async Task<Results<Ok<T>, JsonHttpResult<OperatorError>>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return TypedResults.Ok(await action());
        }
        catch (OperatorFailureException failure)
        {
            return TypedResults.Json(failure.Error, statusCode: MapStatusCode(failure.Error.Code));
        }
    }

    private static int MapStatusCode(string errorCode) =>
        errorCode switch
        {
            ErrorCodes.WindowNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.LockedDesktop => StatusCodes.Status423Locked,
            ErrorCodes.UipiBlocked => StatusCodes.Status409Conflict,
            ErrorCodes.ElevatedTarget => StatusCodes.Status409Conflict,
            ErrorCodes.BlankCapture => StatusCodes.Status409Conflict,
            ErrorCodes.MinimizedRdp => StatusCodes.Status409Conflict,
            ErrorCodes.UnsupportedControl => StatusCodes.Status422UnprocessableEntity,
            ErrorCodes.PowerPointValidationFailed => StatusCodes.Status422UnprocessableEntity,
            ErrorCodes.PowerPointJobNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.PowerPointUnavailable => StatusCodes.Status423Locked,
            ErrorCodes.MailFolderNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.MailRunNotFound => StatusCodes.Status404NotFound,
            ErrorCodes.MailUnavailable => StatusCodes.Status423Locked,
            _ => StatusCodes.Status500InternalServerError,
        };
}
