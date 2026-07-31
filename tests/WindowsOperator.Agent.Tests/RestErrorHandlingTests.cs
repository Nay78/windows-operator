using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WindowsOperator.Agent.Hosting;
using WindowsOperator.Agent.Tests.Fakes;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Tests;

public sealed class RestErrorHandlingTests
{
    [Theory]
    [InlineData(ErrorCodes.AuthRunNotFound)]
    [InlineData(ErrorCodes.BrowserSessionNotFound)]
    [InlineData(ErrorCodes.WorkbenchSessionNotFound)]
    [InlineData(ErrorCodes.PowerPointSessionNotFound)]
    public void ResourceNotFound_MapsToHttpNotFound(string errorCode)
    {
        Assert.Equal(
            (int)HttpStatusCode.NotFound,
            Agent.Api.OperatorHttp.MapStatusCode(errorCode));
    }

    [Fact]
    public async Task UnknownRoute_ReturnsTypedRouteNotFound()
    {
        using var app = BuildApp(new FakeOperatorFacade());
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/v1/does-not-exist");

        await AssertTypedErrorAsync(
            response,
            HttpStatusCode.NotFound,
            ErrorCodes.RouteNotFound,
            OperatorErrorCategory.NotFound,
            retryable: false);
    }

    [Fact]
    public async Task WrongMethod_ReturnsTypedMethodNotAllowed()
    {
        using var app = BuildApp(new FakeOperatorFacade());
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsync("/v1/health", content: null);

        await AssertTypedErrorAsync(
            response,
            HttpStatusCode.MethodNotAllowed,
            ErrorCodes.MethodNotAllowed,
            OperatorErrorCategory.Validation,
            retryable: false);
    }

    [Fact]
    public async Task MalformedJson_ReturnsTypedInvalidRequest()
    {
        using var app = BuildApp(new FakeOperatorFacade());
        await app.StartAsync();
        var client = app.GetTestClient();
        using var content = new StringContent(
            "{\"keys\":",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/v1/input/hotkey", content);

        await AssertTypedErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.InvalidRequest,
            OperatorErrorCategory.Validation,
            retryable: false);
    }

    [Fact]
    public async Task InvalidScreenshotFormat_ReturnsTypedInvalidRequest()
    {
        using var app = BuildApp(new FakeOperatorFacade());
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/v1/windows/42/screenshot?format=gif");

        await AssertTypedErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.InvalidRequest,
            OperatorErrorCategory.Validation,
            retryable: false);
    }

    [Fact]
    public async Task UnexpectedEndpointException_ReturnsSafeTypedInternalError()
    {
        using var app = BuildApp(new FakeOperatorFacade(new InvalidOperationException("secret test detail")));
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/v1/health");

        await AssertTypedErrorAsync(
            response,
            HttpStatusCode.InternalServerError,
            ErrorCodes.InternalError,
            OperatorErrorCategory.Internal,
            retryable: true);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret test detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
    }

    private static WebApplication BuildApp(IOperatorFacade facade) =>
        OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                var existing = services.Single(
                    descriptor => descriptor.ServiceType == typeof(IOperatorFacade));
                services.Remove(existing);
                services.AddSingleton(facade);
            },
            useTestServer: true);

    private static async Task AssertTypedErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        OperatorErrorCategory expectedCategory,
        bool retryable)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var error = await response.Content.ReadFromJsonAsync<OperatorError>(OperatorJson.SerializerOptions);
        Assert.NotNull(error);
        Assert.Equal(expectedCode, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.False(string.IsNullOrWhiteSpace(error.Remediation));
        Assert.False(string.IsNullOrWhiteSpace(error.CorrelationId));
        Assert.Equal(expectedCategory, error.Category);
        Assert.Equal(retryable, error.Retryable);
    }
}
