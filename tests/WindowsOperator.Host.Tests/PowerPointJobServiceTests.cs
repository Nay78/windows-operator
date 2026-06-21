using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Host.Services;

namespace WindowsOperator.Host.Tests;

public sealed class PowerPointJobServiceTests
{
    [Fact]
    public async Task ClaimNextAsync_ReturnsOnlyMatchingQueuedDocument()
    {
        using var workspace = new TestWorkspace();
        var service = CreateService(workspace.Root);

        await service.EnqueueAsync(CreateJob("job-a", "https://tenant/site/a.pptx"), CancellationToken.None);
        await service.EnqueueAsync(CreateJob("job-b", "https://tenant/site/b.pptx"), CancellationToken.None);

        var claimed = await service.ClaimNextAsync(
            new PowerPointClaimJobRequest
            {
                WorkerId = "worker-1",
                DocumentUrl = "https://tenant/site/b.pptx",
            },
            CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal("job-b", claimed!.JobId);
        Assert.Equal("queued", (await service.GetAsync("job-a", CancellationToken.None)).Status);
        var record = await service.GetAsync("job-b", CancellationToken.None);
        Assert.Equal("running", record.Status);
        Assert.Equal("worker-1", record.ClaimedBy);
    }

    [Fact]
    public async Task CompleteAsync_PersistsOfficeJsResult()
    {
        using var workspace = new TestWorkspace();
        var service = CreateService(workspace.Root);

        await service.EnqueueAsync(CreateJob("job-c", "https://tenant/site/c.pptx"), CancellationToken.None);
        await service.ClaimNextAsync(
            new PowerPointClaimJobRequest { DocumentUrl = "https://tenant/site/c.pptx" },
            CancellationToken.None);

        var result = new PowerPointUpdateResult
        {
            JobId = "job-c",
            Status = "succeeded",
            StartedAt = DateTimeOffset.Parse("2026-06-17T12:00:00Z"),
            FinishedAt = DateTimeOffset.Parse("2026-06-17T12:00:01Z"),
            Targets = new[]
            {
                new PowerPointTargetResult("summary-status", "replaceText", "succeeded"),
            },
        };

        var completed = await service.CompleteAsync("job-c", result, CancellationToken.None);

        Assert.Equal("succeeded", completed.Status);
        Assert.NotNull(completed.CompletedAtUtc);
        Assert.Equal("summary-status", completed.Result!.Targets[0].TargetId);
    }

    [Fact]
    public async Task EnqueueAsync_StagesDataUrlArtifactsBehindJobUrl()
    {
        using var workspace = new TestWorkspace();
        var service = CreateService(workspace.Root);
        var job = CreateJob("job-img", "https://tenant/site/image.pptx") with
        {
            Operations = new[]
            {
                new PowerPointUpdateOperation
                {
                    Kind = "replaceImage",
                    TargetId = "hero-image",
                    Artifact = new PowerPointArtifactRef
                    {
                        ArtifactId = "img-1",
                        Url = "data:image/png;base64,AQID",
                        MediaType = "image/png",
                    },
                },
            },
        };

        var record = await service.EnqueueAsync(job, CancellationToken.None);
        var artifact = record.Job.Operations[0].Artifact!;
        var content = await service.GetArtifactAsync("job-img", "img-1", CancellationToken.None);

        Assert.Equal("/v1/powerpoint/jobs/job-img/artifacts/img-1", artifact.Url);
        Assert.Equal("image/png", content.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, content.Bytes);
        Assert.Equal(64, artifact.Sha256!.Length);
    }

    [Theory]
    [MemberData(nameof(InvalidEnqueueJobs))]
    public async Task EnqueueAsync_RejectsInvalidBoundaryPayloads(PowerPointUpdateJob job)
    {
        using var workspace = new TestWorkspace();
        var service = CreateService(workspace.Root);

        await AssertPowerPointValidationFailedAsync(
            () => service.EnqueueAsync(job, CancellationToken.None));
    }

    [Fact]
    public async Task EnqueueAsync_AllowsEmptyTextOnlyWhenExplicit()
    {
        using var workspace = new TestWorkspace();
        var service = CreateService(workspace.Root);
        var job = CreateJob("job-empty", "https://tenant/site/empty.pptx") with
        {
            Operations = new[]
            {
                new PowerPointUpdateOperation
                {
                    Kind = "replaceText",
                    TargetId = "summary-status",
                    Text = "",
                    Mode = "plain",
                    AllowEmpty = true,
                },
            },
        };

        var record = await service.EnqueueAsync(job, CancellationToken.None);

        Assert.Equal("queued", record.Status);
    }

    [Fact]
    public async Task CompleteAsync_RejectsMismatchedResultJobId()
    {
        using var workspace = new TestWorkspace();
        var service = CreateService(workspace.Root);
        await service.EnqueueAsync(CreateJob("job-match", "https://tenant/site/match.pptx"), CancellationToken.None);

        await AssertPowerPointValidationFailedAsync(
            () => service.CompleteAsync(
                "job-match",
                CreateResult("other-job", "succeeded"),
                CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(InvalidCompleteResults))]
    public async Task CompleteAsync_RejectsInvalidResults(PowerPointUpdateResult result)
    {
        using var workspace = new TestWorkspace();
        var service = CreateService(workspace.Root);
        await service.EnqueueAsync(CreateJob("job-result", "https://tenant/site/result.pptx"), CancellationToken.None);

        await AssertPowerPointValidationFailedAsync(
            () => service.CompleteAsync("job-result", result, CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_MapsPartialResultToFailedRecordStatus()
    {
        using var workspace = new TestWorkspace();
        var service = CreateService(workspace.Root);
        await service.EnqueueAsync(CreateJob("job-partial", "https://tenant/site/partial.pptx"), CancellationToken.None);

        var completed = await service.CompleteAsync(
            "job-partial",
            CreateResult("job-partial", "partial"),
            CancellationToken.None);

        Assert.Equal("failed", completed.Status);
        Assert.Equal("partial", completed.Result!.Status);
    }

    [Theory]
    [InlineData("", "operator message")]
    [InlineData("UPDATE_FAILED", "")]
    public async Task FailAsync_RejectsInvalidErrors(string code, string operatorMessage)
    {
        using var workspace = new TestWorkspace();
        var service = CreateService(workspace.Root);
        await service.EnqueueAsync(CreateJob("job-fail", "https://tenant/site/fail.pptx"), CancellationToken.None);

        await AssertPowerPointValidationFailedAsync(
            () => service.FailAsync(
                "job-fail",
                new PowerPointUpdateError(code, false, operatorMessage),
                CancellationToken.None));
    }

    private static PowerPointJobService CreateService(string root) =>
        new(
            new HttpClient(),
            Options.Create(new PowerPointAddInOptions { StateRoot = root }));

    public static TheoryData<PowerPointUpdateJob> InvalidEnqueueJobs() =>
        new()
        {
            CreateJob("bad/job", "https://tenant/site/deck.pptx"),
            CreateJob("Job-Upper", "https://tenant/site/deck.pptx"),
            CreateJob("job.", "https://tenant/site/deck.pptx"),
            CreateJob(".job", "https://tenant/site/deck.pptx"),
            CreateJob("con", "https://tenant/site/deck.pptx"),
            CreateJob("job-no-requester", "https://tenant/site/deck.pptx") with { RequestedBy = " " },
            CreateJob("job-null-operations", "https://tenant/site/deck.pptx") with { Operations = null! },
            CreateJob("job-unknown-kind", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    TextOperation("replaceShape", text: "Hello"),
                },
            },
            CreateJob("job-missing-text", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    TextOperation(overrideText: null),
                },
            },
            CreateJob("job-blank-text", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    TextOperation(text: " "),
                },
            },
            CreateJob("job-bad-mode", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    TextOperation(text: "Hello", mode: "html"),
                },
            },
            CreateJob("job-missing-artifact", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    new PowerPointUpdateOperation
                    {
                        Kind = "replaceImage",
                        TargetId = "hero-image",
                    },
                },
            },
            CreateJob("job-bad-fit", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    ImageOperation(fit: "stretch"),
                },
            },
            CreateJob("job-bad-artifact-id", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    ImageOperation(artifact: ValidArtifact() with { ArtifactId = "bad/id" }),
                },
            },
            CreateJob("job-uppercase-artifact-id", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    ImageOperation(artifact: ValidArtifact() with { ArtifactId = "Img-1" }),
                },
            },
            CreateJob("job-trailing-dot-artifact-id", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    ImageOperation(artifact: ValidArtifact() with { ArtifactId = "img." }),
                },
            },
            CreateJob("job-device-artifact-id", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    ImageOperation(artifact: ValidArtifact() with { ArtifactId = "con" }),
                },
            },
            CreateJob("job-bad-media", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    ImageOperation(artifact: ValidArtifact() with { MediaType = "image/gif" }),
                },
            },
            CreateJob("job-expired-artifact", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    ImageOperation(artifact: ValidArtifact() with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) }),
                },
            },
            CreateJob("job-bad-sha", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    ImageOperation(artifact: ValidArtifact() with { Sha256 = "not-a-sha" }),
                },
            },
            CreateJob("job-bad-data-url", "https://tenant/site/deck.pptx") with
            {
                Operations = new[]
                {
                    ImageOperation(artifact: ValidArtifact() with { Url = "data:image/png;base64,not-base64!!!" }),
                },
            },
        };

    public static TheoryData<PowerPointUpdateResult> InvalidCompleteResults() =>
        new()
        {
            CreateResult("job-result", "succeeded") with { Targets = null! },
            CreateResult("job-result", "done"),
            CreateResult("job-result", "succeeded") with
            {
                Targets = new[]
                {
                    new PowerPointTargetResult("summary-status", "replaceShape", "succeeded"),
                },
            },
            CreateResult("job-result", "succeeded") with
            {
                Targets = new[]
                {
                    new PowerPointTargetResult("summary-status", "replaceText", "done"),
                },
            },
        };

    private static PowerPointUpdateJob CreateJob(string jobId, string expectedDocumentUrl) =>
        new()
        {
            JobId = jobId,
            ExpectedDocumentUrl = expectedDocumentUrl,
            RequestedBy = "test",
            CreatedAt = DateTimeOffset.Parse("2026-06-17T12:00:00Z"),
            Operations = new[]
            {
                new PowerPointUpdateOperation
                {
                    Kind = "replaceText",
                    TargetId = "summary-status",
                    Text = "On track",
                    Mode = "plain",
                },
            },
        };

    private static PowerPointUpdateOperation TextOperation(
        string kind = "replaceText",
        string? text = "Hello",
        string? mode = "plain",
        string? overrideText = "use-text") =>
        new()
        {
            Kind = kind,
            TargetId = "summary-status",
            Text = overrideText == "use-text" ? text : overrideText,
            Mode = mode,
        };

    private static PowerPointUpdateOperation ImageOperation(
        PowerPointArtifactRef? artifact = null,
        string? fit = "contain") =>
        new()
        {
            Kind = "replaceImage",
            TargetId = "hero-image",
            Artifact = artifact ?? ValidArtifact(),
            Fit = fit,
        };

    private static PowerPointArtifactRef ValidArtifact() =>
        new()
        {
            ArtifactId = "img-1",
            Url = "data:image/png;base64,AQID",
            MediaType = "image/png",
        };

    private static PowerPointUpdateResult CreateResult(string jobId, string status) =>
        new()
        {
            JobId = jobId,
            Status = status,
            StartedAt = DateTimeOffset.Parse("2026-06-17T12:00:00Z"),
            FinishedAt = DateTimeOffset.Parse("2026-06-17T12:00:01Z"),
            Targets = new[]
            {
                new PowerPointTargetResult("summary-status", "replaceText", "succeeded"),
            },
        };

    private static async Task AssertPowerPointValidationFailedAsync(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<OperatorFailureException>(action);
        Assert.Equal(ErrorCodes.PowerPointValidationFailed, exception.Error.Code);
    }

    private sealed class TestWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "windows-operator-host-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
