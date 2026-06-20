using Microsoft.Extensions.Options;
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

    private static PowerPointJobService CreateService(string root) =>
        new(
            new HttpClient(),
            Options.Create(new PowerPointAddInOptions { StateRoot = root }));

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
