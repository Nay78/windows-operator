using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Host.Api;

public static class HostOperatorErrorHandling
{
    public static IApplicationBuilder UseHostOperatorErrorHandling(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
                if (TryGetUnhandledRequestError(context, out var error))
                {
                    await WriteErrorAsync(
                        context,
                        error,
                        context.Response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (OperatorFailureException failure)
            {
                await WriteErrorAsync(
                    context,
                    failure.Error,
                    HostOperatorHttp.MapStatusCode(failure.Error.Code));
            }
            catch (BadHttpRequestException badRequest)
            {
                await WriteErrorAsync(
                    context,
                    OperatorErrors.InvalidRequest("Request body or parameters could not be bound."),
                    badRequest.StatusCode is >= 400 and < 500
                        ? badRequest.StatusCode
                        : StatusCodes.Status400BadRequest);
            }
            catch (Exception exception)
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("WindowsOperator.Host.Api");
                logger.LogError(
                    exception,
                    "Unhandled endpoint exception. CorrelationId={CorrelationId}",
                    context.TraceIdentifier);
                await WriteErrorAsync(
                    context,
                    OperatorErrors.InternalError(),
                    StatusCodes.Status500InternalServerError);
            }
        });

    private static bool TryGetUnhandledRequestError(
        HttpContext context,
        out OperatorError error)
    {
        error = OperatorErrors.InvalidRequest("Request body or parameters could not be bound.");
        if (context.Response.HasStarted || !string.IsNullOrWhiteSpace(context.Response.ContentType))
        {
            return false;
        }

        error = context.Response.StatusCode switch
        {
            StatusCodes.Status404NotFound => OperatorErrors.RouteNotFound(
                $"{context.Request.Method} {context.Request.Path}"),
            StatusCodes.Status405MethodNotAllowed => OperatorErrors.MethodNotAllowed(
                $"{context.Request.Method} {context.Request.Path}"),
            _ => error,
        };
        return context.Response.StatusCode is
            StatusCodes.Status400BadRequest or
            StatusCodes.Status404NotFound or
            StatusCodes.Status405MethodNotAllowed or
            StatusCodes.Status413PayloadTooLarge or
            StatusCodes.Status415UnsupportedMediaType or
            StatusCodes.Status422UnprocessableEntity;
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        OperatorError error,
        int statusCode)
    {
        if (context.Response.HasStarted)
        {
            throw new InvalidOperationException("Cannot write an operator error after the response has started.");
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        var correlatedError = string.IsNullOrWhiteSpace(error.CorrelationId)
            ? error with { CorrelationId = context.TraceIdentifier }
            : error;
        await context.Response.WriteAsJsonAsync(
            correlatedError,
            OperatorJson.SerializerOptions,
            contentType: "application/json",
            cancellationToken: context.RequestAborted);
    }
}
