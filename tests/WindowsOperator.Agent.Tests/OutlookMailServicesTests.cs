using WindowsOperator.Agent.Services;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Agent.Tests;

public sealed class OutlookMailServicesTests
{
    [Fact]
    public void SearchFolderSnapshot_MaterializesAndSortsWithoutDynamicBinderFailure()
    {
        var folder = new FakeFolder(
            new FakeMailItem("id-2", "Older", new DateTime(2026, 5, 16, 12, 0, 0), new DateTime(2026, 5, 17, 10, 0, 0), attachmentCount: 0),
            new FakeMailItem("id-3", "Filtered", new DateTime(2026, 5, 17, 10, 0, 0), new DateTime(2026, 5, 17, 15, 0, 0), attachmentCount: 0),
            new FakeMailItem("id-1", "Newest", new DateTime(2026, 5, 17, 14, 0, 0), new DateTime(2026, 5, 17, 14, 30, 0), attachmentCount: 0));
        var request = new MailSearchRequest
        {
            FolderPath = "mailbox/Alimentacion",
            MaxResults = 2,
            SubjectContains = "e",
            IncludeAttachmentDetails = false,
        };

        var messages = OutlookMailComService.SearchFolderSnapshot(
            folder,
            "mailbox/Alimentacion",
            request);

        Assert.Collection(
            messages,
            first =>
            {
                Assert.Equal("id-3", first.MessageId);
                Assert.Equal("Filtered", first.Subject);
                Assert.Equal(new DateTimeOffset(new DateTime(2026, 5, 17, 15, 0, 0)), first.ModifiedTime);
            },
            second =>
            {
                Assert.Equal("id-1", second.MessageId);
                Assert.Equal("Newest", second.Subject);
                Assert.Equal(new DateTimeOffset(new DateTime(2026, 5, 17, 14, 30, 0)), second.ModifiedTime);
            });
    }

    [Fact]
    public void SearchSortField_UsesLastModificationTimeForFolderView()
    {
        var request = new MailSearchRequest
        {
            FolderPath = "mailbox/Alimentacion",
        };

        Assert.Equal("[LastModificationTime]", OutlookMailComService.SearchSortField(request));
        Assert.Equal(
            "[ReceivedTime]",
            OutlookMailComService.SearchSortField(request with { ReceivedAfterUtc = DateTimeOffset.UtcNow }));
    }

    [Theory]
    [InlineData("auto", true, 0, true)]
    [InlineData("auto", false, 0, false)]
    [InlineData("cached", true, 0, false)]
    [InlineData("fresh", true, 0, false)]
    [InlineData("auto", true, 2, false)]
    public void ShouldRetryEmptyAutoSearch_FollowsExpectedRules(
        string freshness,
        bool refreshSkippedFreshCache,
        int resultCount,
        bool expected)
    {
        var actual = OutlookMailComService.ShouldRetryEmptyAutoSearch(
            freshness,
            refreshSkippedFreshCache,
            resultCount);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void HasRetryBudget_RequiresSyncWaitPlusSafetyMargin()
    {
        var startedAt = new DateTimeOffset(2026, 5, 17, 21, 0, 0, TimeSpan.Zero);
        var requestBudget = TimeSpan.FromSeconds(90);
        var requiredBudget = TimeSpan.FromSeconds(30);

        Assert.True(OutlookMailComService.HasRetryBudget(
            startedAt,
            startedAt.AddSeconds(59),
            requestBudget,
            requiredBudget));

        Assert.False(OutlookMailComService.HasRetryBudget(
            startedAt,
            startedAt.AddSeconds(61),
            requestBudget,
            requiredBudget));
    }

    [Fact]
    public void BuildWorkerTimeoutDetail_IncludesLastProgressStage()
    {
        var progressPath = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(
                progressPath,
                new[]
                {
                    "2026-05-17T21:06:35.7966499+00:00\tensure_fresh_start:fresh",
                    "2026-05-17T21:07:20.8906168+00:00\tmail_operation_start",
                });

            var detail = OutlookMailService.BuildWorkerTimeoutDetail(
                "op-123",
                @"C:\state\mail-worker\op-123",
                @"C:\state\mail-worker\op-123\request.json",
                @"C:\state\mail-worker\op-123\response.json",
                progressPath,
                4321,
                TimeSpan.FromSeconds(90),
                90123);

            Assert.Contains("OperationId=op-123", detail);
            Assert.Contains("WorkerPid=4321", detail);
            Assert.Contains("LastStage=mail_operation_start", detail);
            Assert.Contains(progressPath, detail);
        }
        finally
        {
            File.Delete(progressPath);
        }
    }

    public sealed class FakeFolder
    {
        public FakeFolder(params FakeMailItem[] items)
        {
            Items = new FakeItems(items);
        }

        public FakeItems Items { get; }
    }

    public sealed class FakeItems
    {
        private List<FakeMailItem> _items;

        public FakeItems(IEnumerable<FakeMailItem> items)
        {
            _items = items.ToList();
        }

        public int Count => _items.Count;

        public FakeMailItem Item(int index) => _items[index - 1];

        public void Sort(string field, bool descending)
        {
            _items = field switch
            {
                "[LastModificationTime]" when descending => _items.OrderByDescending(item => item.LastModificationTime).ToList(),
                "[LastModificationTime]" => _items.OrderBy(item => item.LastModificationTime).ToList(),
                "[ReceivedTime]" when descending => _items.OrderByDescending(item => item.ReceivedTime).ToList(),
                "[ReceivedTime]" => _items.OrderBy(item => item.ReceivedTime).ToList(),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected sort field: {field}"),
            };
        }
    }

    public sealed class FakeMailItem
    {
        public FakeMailItem(string entryId, string subject, DateTime receivedTime, DateTime lastModificationTime, int attachmentCount)
        {
            EntryID = entryId;
            Subject = subject;
            ReceivedTime = receivedTime;
            LastModificationTime = lastModificationTime;
            Attachments = new FakeAttachments(attachmentCount);
        }

        public string EntryID { get; }

        public string Subject { get; }

        public DateTime ReceivedTime { get; }

        public DateTime LastModificationTime { get; }

        public FakeAttachments Attachments { get; }
    }

    public sealed class FakeAttachments
    {
        public FakeAttachments(int count)
        {
            Count = count;
        }

        public int Count { get; }
    }
}
