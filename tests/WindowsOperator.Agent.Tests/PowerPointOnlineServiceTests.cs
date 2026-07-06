using Microsoft.Extensions.Options;
using WindowsOperator.Agent.Services;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Tests;

public sealed class PowerPointOnlineServiceTests
{
    [Fact]
    public async Task StartOnlineSessionAsync_OpensEdgeWorkSession_AndCapturesEvidence()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var edge = new FakeEdgeBrowserService();
        var uia = new FakeUiAutomationService
        {
            Elements =
            [
                new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
            ],
        };
        var service = new PowerPointOnlineService(edge, new FakeInputService(), new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        var result = await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = true,
                WaitSeconds = 9,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineSessionStatus.Ready, result.Status);
        Assert.Equal("ppt-session", workbench.LastOpenRequest!.SessionId);
        Assert.Equal(BrowserEdgeProfileMode.Work, workbench.LastOpenRequest.ProfileMode);
        Assert.Equal("runs/ppt-session", result.ArtifactRoot!.RelativePath);
        Assert.Single(result.Evidence);
        Assert.Equal(4, result.CurrentSlide);
        Assert.Equal(71, result.SlideCount);
        Assert.Equal("editing", result.EditMode);
        Assert.Equal("saved", result.SaveState);
        Assert.Contains("powerpoint_online_uia_observed", result.Actions);
    }

    [Fact]
    public async Task StartOnlineSessionAsync_ClassifiesAuthBlocker()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService
        {
            NextOpenState = FakeWorkbenchService.EdgeState(
                "ppt-session",
                "https://login.microsoftonline.com/",
                "Sign in to your account",
                "Stay signed in",
                true),
        };
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), new FakeInputService(), new FakeAddInHostProbe(), new FakeUiAutomationService(), new WorkbenchRunStore(env.Options), workbench);

        var result = await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineSessionStatus.BlockedAuth, result.Status);
        Assert.Equal(ErrorCodes.AuthUnavailable, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task StartOnlineSessionAsync_ReusedSession_NavigatesWhenQueryIdentityDiffers()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var edge = new FakeEdgeBrowserService();
        var service = new PowerPointOnlineService(edge, new FakeInputService(), new FakeAddInHostProbe(), new FakeUiAutomationService(), new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?sourcedoc=%7Bfirst%7D&action=edit",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?sourcedoc=%7Bsecond%7D&action=edit",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        Assert.NotNull(edge.LastNavigateRequest);
        Assert.Contains("sourcedoc={second}", edge.LastNavigateRequest!.Url);
    }

    [Fact]
    public async Task StartOnlineSessionAsync_RecreatesClosedCachedSession_InsteadOfReusingMetadata()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var edge = new FakeEdgeBrowserService
        {
            SessionStateFactory = sessionId => FakeWorkbenchService.EdgeState(
                sessionId,
                "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                "Deck - PowerPoint",
                null,
                false),
        };
        var service = new PowerPointOnlineService(edge, new FakeInputService(), new FakeAddInHostProbe(), new FakeUiAutomationService(), new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineSessionStatus.Ready, result.Status);
        Assert.Equal(2, workbench.OpenEdgeUrlCallCount);
        Assert.Null(edge.LastNavigateRequest);
        Assert.Contains("session_recreated_stale_closed", result.Actions);
        Assert.DoesNotContain("session_reused", result.Actions);
    }

    [Fact]
    public async Task SelectOnlineSlideAsync_FallsBackToThumbnailClick_WhenDomUnavailable()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var edge = new FakeEdgeBrowserService { DomShouldFail = true };
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Slide 1 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("5", "Saving to cloud", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("6", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("7", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("8", "Saving to cloud", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("9", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(edge, input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.SelectOnlineSlideAsync(
            "ppt-session",
            new PowerPointOnlineSlideSelectRequest
            {
                SlideNumber = 4,
                Capture = true,
                WaitSeconds = 0,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("[aria-label='Slide 4']", edge.FirstDomClickRequest!.Selector);
        Assert.NotNull(input.LastClick);
        Assert.Equal(132, input.LastClick!.X);
        Assert.Equal(675, input.LastClick.Y);
        Assert.Contains("slide_select_dom_unavailable:4", result.Actions);
        Assert.Contains("slide_select_thumbnail_click:4:132:675", result.Actions);
        Assert.Contains("slide_click_dispatched:4", result.Actions);
        Assert.Contains("slide_select_verified:4", result.Actions);
        Assert.Single(result.Evidence);
        Assert.Equal(4, result.CurrentSlide);
        Assert.Equal(71, result.SlideCount);
        Assert.Equal("editing", result.EditMode);
        Assert.Equal("saving", result.SaveState);
    }

    [Fact]
    public async Task SelectOnlineSlideAsync_SkipsDomAttempts_WhenDevToolsRecentlyUnavailable()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var edge = new FakeEdgeBrowserService
        {
            SessionStateFactory = sessionId => FakeWorkbenchService.EdgeState(
                sessionId,
                "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                "Deck - PowerPoint",
                "PowerPoint for the web",
                true) with
            {
                Actions = new[] { "session_state_observed", "devtools_status:target_unavailable" },
            },
        };
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Slide 1 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("5", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("6", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("7", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("8", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("9", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(edge, input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.SelectOnlineSlideAsync(
            "ppt-session",
            new PowerPointOnlineSlideSelectRequest
            {
                SlideNumber = 4,
                Capture = false,
                WaitSeconds = 0,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(edge.FirstDomClickRequest);
        Assert.Equal(0, edge.DomClickCount);
        Assert.NotNull(input.LastClick);
        Assert.Contains("slide_select_dom_skipped:4", result.Actions);
        Assert.Contains("devtools_status:target_unavailable", result.Actions);
        Assert.Contains("slide_select_thumbnail_click:4:132:675", result.Actions);
        Assert.Contains("slide_select_verified:4", result.Actions);
    }

    [Fact]
    public async Task SelectOnlineSlideAsync_UsesDomFirst_WhenDevToolsStatusUnknown()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var edge = new FakeEdgeBrowserService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("5", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("6", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(edge, input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.SelectOnlineSlideAsync(
            "ppt-session",
            new PowerPointOnlineSlideSelectRequest
            {
                SlideNumber = 4,
                Capture = false,
                WaitSeconds = 0,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(edge.FirstDomClickRequest);
        Assert.Equal(1, edge.DomClickCount);
        Assert.Null(input.LastClick);
        Assert.Contains("slide_click_dispatched:4", result.Actions);
        Assert.DoesNotContain("slide_select_dom_skipped:4", result.Actions);
    }

    [Fact]
    public async Task SelectOnlineSlideAsync_UsesDomFirst_WhenLatestDevToolsStatusIsReady()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var edge = new FakeEdgeBrowserService
        {
            SessionStateFactory = sessionId => FakeWorkbenchService.EdgeState(
                sessionId,
                "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                "Deck - PowerPoint",
                "PowerPoint for the web",
                true) with
            {
                Actions = new[]
                {
                    "session_state_observed",
                    "devtools_status:target_unavailable",
                    "devtools_status:ready",
                },
            },
        };
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("5", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("6", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(edge, input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.SelectOnlineSlideAsync(
            "ppt-session",
            new PowerPointOnlineSlideSelectRequest
            {
                SlideNumber = 4,
                Capture = false,
                WaitSeconds = 0,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(edge.FirstDomClickRequest);
        Assert.Equal(1, edge.DomClickCount);
        Assert.DoesNotContain("slide_select_dom_skipped:4", result.Actions);
    }

    [Fact]
    public async Task SelectOnlineSlideAsync_CorrectsWithKeyboard_WhenThumbnailClickSelectsNearbySlide()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var edge = new FakeEdgeBrowserService { DomShouldFail = true };
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Slide 1 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "Slide 2 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("5", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("6", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("7", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("8", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("9", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(edge, input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.SelectOnlineSlideAsync(
            "ppt-session",
            new PowerPointOnlineSlideSelectRequest
            {
                SlideNumber = 4,
                Capture = false,
                WaitSeconds = 0,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(new[] { "pagedown", "pagedown" }, input.HotkeyHistory.Select(keys => keys.Single()).ToArray());
        Assert.Contains("slide_select_keyboard_correction:2:4:pagedown:2", result.Actions);
        Assert.Contains("slide_select_keyboard_correction_dispatched", result.Actions);
        Assert.Contains("slide_select_verified:4", result.Actions);
        Assert.Equal(4, result.CurrentSlide);
    }

    [Fact]
    public async Task SelectOnlineSlideAsync_FailsVerification_WhenObservedSlideDoesNotMatchRequest()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var edge = new FakeEdgeBrowserService { DomShouldFail = true };
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Slide 1 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "Slide 2 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("5", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("6", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("7", "Slide 3 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("8", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("9", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(edge, input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.SelectOnlineSlideAsync(
            "ppt-session",
            new PowerPointOnlineSlideSelectRequest
            {
                SlideNumber = 4,
                Capture = false,
                WaitSeconds = 0,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("slide_select_verification_failed:3:4", result.Actions);
        Assert.Equal(ErrorCodes.PowerPointUnavailable, Assert.Single(result.Errors).Code);
        Assert.Equal(3, result.CurrentSlide);
    }

    [Fact]
    public async Task PrepareOnlineTemplateAsync_ClicksVisibleTaskPaneButton()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            Elements =
            [
                new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("4", "Prepare Template", string.Empty, "Button", true, false, new WindowBounds(100, 200, 80, 40)),
            ],
        };
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.PrepareOnlineTemplateAsync(
            "ppt-session",
            new PowerPointOnlineTemplateRequest
            {
                Capture = true,
                Label = "template-prepare",
                WaitSeconds = 0,
                AllowDeckMutation = true,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(140, input.LastClick!.X);
        Assert.Equal(220, input.LastClick.Y);
        Assert.Contains("template_prepare_requested", result.Actions);
        Assert.Contains("template_prepare_click_dispatched", result.Actions);
        Assert.Equal("runs/ppt-session/screenshots/template-prepare.png", Assert.Single(result.Evidence).Artifact.RelativePath);
        Assert.Equal(4, result.CurrentSlide);
        Assert.Equal("saved", result.SaveState);
    }

    [Fact]
    public async Task RunOnlinePendingJobAsync_ClicksVisibleTaskPaneButton()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            Elements =
            [
                new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("4", "Run Pending Job", string.Empty, "Button", true, false, new WindowBounds(100, 200, 120, 40)),
            ],
        };
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.RunOnlinePendingJobAsync(
            "ppt-session",
            new PowerPointOnlineAddInCommandRequest
            {
                Capture = true,
                Label = "run-pending-job",
                WaitSeconds = 0,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(160, input.LastClick!.X);
        Assert.Equal(220, input.LastClick.Y);
        Assert.Contains("addin_run_pending_job_requested", result.Actions);
        Assert.Contains("addin_run_pending_job_click_dispatched", result.Actions);
        Assert.Equal("runs/ppt-session/screenshots/run-pending-job.png", Assert.Single(result.Evidence).Artifact.RelativePath);
    }

    [Fact]
    public async Task RunOnlinePendingJobAsync_DispatchesCommandSignal_BeforeUiAutomationFallback()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var targetedQueryCount = 0;
        var uia = new FakeUiAutomationService
        {
            QueryHandler = query =>
            {
                if (string.Equals(query.Name, "Run Pending Job", StringComparison.Ordinal))
                {
                    targetedQueryCount++;
                }

                return
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("4", "Run Pending Job", string.Empty, "Button", true, false, new WindowBounds(100, 200, 120, 40)),
                ];
            },
        };
        var devTools = new FakeEdgeDevToolsService(
            new EdgePageTarget("target-1", "Deck - PowerPoint", "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1", "ws://edge"),
            new EdgeDevToolsEvaluation(true, false, null, """{"accepted":true}""", null));
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench, devTools);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.RunOnlinePendingJobAsync(
            "ppt-session",
            new PowerPointOnlineAddInCommandRequest
            {
                WaitSeconds = 0,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(input.LastClick);
        Assert.Equal(0, targetedQueryCount);
        Assert.Contains("addin_run_pending_job_command_signal_dispatched", result.Actions);
        Assert.DoesNotContain("addin_run_pending_job_command_signal_unavailable", result.Actions);
        Assert.DoesNotContain("addin_run_pending_job_click_dispatched", result.Actions);
        Assert.Single(devTools.Evaluations);
    }

    [Fact]
    public async Task RunOnlinePendingJobAsync_UsesSiblingButtonFallback_WhenRunButtonMissingFromUia()
    {
        using var env = new ExchangeRootScope();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            Elements =
            [
                new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("4", "Cleanup Template", string.Empty, "Button", true, false, new WindowBounds(100, 200, 80, 40)),
            ],
        };
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), new FakeWorkbenchService());

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.RunOnlinePendingJobAsync(
            "ppt-session",
            new PowerPointOnlineAddInCommandRequest { WaitSeconds = 0 },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(230, input.LastClick!.X);
        Assert.Equal(220, input.LastClick.Y);
        Assert.Contains("addin_run_pending_job_click_fallback:cleanup_template_sibling", result.Actions);
        Assert.Contains("addin_run_pending_job_click_dispatched", result.Actions);
    }

    [Fact]
    public async Task RunOnlinePendingJobAsync_FallsBackToUiAutomation_WhenCommandSignalUnavailable()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            Elements =
            [
                new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("4", "Run Pending Job", string.Empty, "Button", true, false, new WindowBounds(100, 200, 120, 40)),
            ],
        };
        var devTools = new FakeEdgeDevToolsService(
            new EdgePageTarget("target-1", "Deck - PowerPoint", "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1", "ws://edge"),
            EdgeDevToolsEvaluation.Failed("no_taskpane_frames"));
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench, devTools);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.RunOnlinePendingJobAsync(
            "ppt-session",
            new PowerPointOnlineAddInCommandRequest
            {
                WaitSeconds = 0,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(160, input.LastClick!.X);
        Assert.Equal(220, input.LastClick.Y);
        Assert.Contains("addin_run_pending_job_command_signal_unavailable", result.Actions);
        Assert.Contains("addin_run_pending_job_click_dispatched", result.Actions);
        Assert.Contains("addin_run_pending_job_command_signal_detail:no_taskpane_frames", result.Warnings);
        Assert.Single(devTools.Evaluations);
    }

    [Fact]
    public async Task RunOnlinePendingJobAsync_RetriesUntilButtonAppears()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var targetedQueryCount = 0;
        var uia = new FakeUiAutomationService
        {
            QueryHandler = query =>
            {
                if (string.Equals(query.Name, "Run Pending Job", StringComparison.Ordinal))
                {
                    targetedQueryCount++;
                    return targetedQueryCount >= 2
                    ? [new UiElementRef("7", "Run Pending Job", string.Empty, "Button", true, false, new WindowBounds(100, 200, 120, 40))]
                    : [];
                }

                return
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ];
            },
        };
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.RunOnlinePendingJobAsync(
            "ppt-session",
            new PowerPointOnlineAddInCommandRequest
            {
                WaitSeconds = 0,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(160, input.LastClick!.X);
        Assert.Equal(220, input.LastClick.Y);
        Assert.Contains("addin_run_pending_job_button_observation_retry:1", result.Actions);
        Assert.Contains("addin_run_pending_job_button_observed_after_retry:1", result.Actions);
        Assert.Contains("addin_run_pending_job_click_dispatched", result.Actions);
    }

    [Fact]
    public async Task RunOnlinePendingJobAsync_UsesTargetedButtonQuery_WhenBroadQueryMissesButton()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var button = new UiElementRef("4", "Run Pending Job", string.Empty, "Button", true, false, new WindowBounds(100, 200, 120, 40));
        var uia = new FakeUiAutomationService
        {
            QueryHandler = query =>
            {
                if (string.Equals(query.Name, "Run Pending Job", StringComparison.Ordinal))
                {
                    return [button];
                }

                return
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ];
            },
        };
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.RunOnlinePendingJobAsync(
            "ppt-session",
            new PowerPointOnlineAddInCommandRequest
            {
                WaitSeconds = 0,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(160, input.LastClick!.X);
        Assert.Equal(220, input.LastClick.Y);
        Assert.DoesNotContain(result.Actions, action => action.StartsWith("addin_run_pending_job_button_observation_retry:", StringComparison.Ordinal));
        Assert.Contains("addin_run_pending_job_click_dispatched", result.Actions);
    }

    [Fact]
    public async Task PrepareOnlineTemplateAsync_RejectsWhenDeckMutationNotAllowed()
    {
        using var env = new ExchangeRootScope();
        var input = new FakeInputService();
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), input, new FakeAddInHostProbe(), new FakeUiAutomationService(), new WorkbenchRunStore(env.Options), new FakeWorkbenchService());

        var ex = await Assert.ThrowsAsync<OperatorFailureException>(() =>
            service.PrepareOnlineTemplateAsync(
                "ppt-session",
                new PowerPointOnlineTemplateRequest
                {
                    WaitSeconds = 0,
                    AllowDeckMutation = false,
                },
                CancellationToken.None));

        Assert.Equal(ErrorCodes.PowerPointValidationFailed, ex.Error.Code);
        Assert.Contains("allowDeckMutation", ex.Error.Details?["detail"]);
        Assert.Null(input.LastClick);
    }

    [Fact]
    public async Task CleanupOnlineTemplateAsync_ReturnsStructuredError_WhenButtonMissing()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            Elements =
            [
                new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
            ],
        };
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.CleanupOnlineTemplateAsync(
            "ppt-session",
            new PowerPointOnlineTemplateRequest
            {
                WaitSeconds = 0,
                AllowDeckMutation = true,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(input.LastClick);
        Assert.Contains("template_cleanup_button_not_found", result.Actions);
        Assert.Equal(ErrorCodes.PowerPointUnavailable, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task RunOnlinePendingJobAsync_ReturnsRetryEvidence_WhenButtonNeverAppears()
    {
        using var env = new ExchangeRootScope();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            Elements =
            [
                new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
            ],
        };
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), input, new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), new FakeWorkbenchService());

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.RunOnlinePendingJobAsync(
            "ppt-session",
            new PowerPointOnlineAddInCommandRequest { WaitSeconds = 0 },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(input.LastClick);
        Assert.Contains(result.Actions, action => action.StartsWith("addin_run_pending_job_button_observation_retry:", StringComparison.Ordinal));
        Assert.Contains("addin_run_pending_job_button_observation_timeout", result.Actions);
        Assert.Contains("addin_run_pending_job_button_not_found", result.Actions);
        Assert.Equal(ErrorCodes.PowerPointUnavailable, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task CleanupOnlineSessionAsync_MarksClosedWhenBrowserMissing()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService { CleanupThrows = true };
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), new FakeInputService(), new FakeAddInHostProbe(), new FakeUiAutomationService(), new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.CleanupOnlineSessionAsync("ppt-session", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineSessionStatus.Closed, result.Status);
        Assert.Contains("powerpoint_online_cleanup_assumed_closed", result.Actions);
    }

    [Fact]
    public async Task CleanupOnlineSessionAsync_VerifiesClosedState_FromWorkbenchCleanup()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService
        {
            CleanupState = FakeWorkbenchService.EdgeState(
                "ppt-session",
                "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                "Deck - PowerPoint",
                null,
                false),
        };
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), new FakeInputService(), new FakeAddInHostProbe(), new FakeUiAutomationService(), new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.CleanupOnlineSessionAsync("ppt-session", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineSessionStatus.Closed, result.Status);
        Assert.Contains("powerpoint_online_cleanup", result.Actions);
        Assert.Contains("powerpoint_online_cleanup_verified_closed", result.Actions);
        Assert.DoesNotContain("cleanup_not_postverified", result.Warnings);
        Assert.Equal(workbench.CleanupState.Errors, result.Warnings);
    }

    [Fact]
    public async Task CleanupOnlineSessionAsync_ReturnsFailed_WhenCleanupStateStillAlive()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService
        {
            CleanupState = FakeWorkbenchService.EdgeState(
                "ppt-session",
                "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                "Deck - PowerPoint",
                "PowerPoint for the web",
                true) with
            {
                Errors = new[] { "edge_cleanup_timeout" },
            },
        };
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), new FakeInputService(), new FakeAddInHostProbe(), new FakeUiAutomationService(), new WorkbenchRunStore(env.Options), workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.CleanupOnlineSessionAsync("ppt-session", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineSessionStatus.Failed, result.Status);
        Assert.Contains("powerpoint_online_cleanup", result.Actions);
        Assert.Contains("powerpoint_online_cleanup_still_alive", result.Actions);
        Assert.Contains("cleanup_still_alive", result.Warnings);
        Assert.Contains("edge_cleanup_timeout", result.Warnings);
        Assert.Equal(ErrorCodes.PowerPointUnavailable, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task StartOnlineSessionAsync_UiaFailure_DoesNotBlockReadySession()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var uia = new FakeUiAutomationService
        {
            QueryException = new OperatorFailureException(OperatorErrors.PowerPointUnavailable("uia backend unavailable")),
        };
        var service = new PowerPointOnlineService(new FakeEdgeBrowserService(), new FakeInputService(), new FakeAddInHostProbe(), uia, new WorkbenchRunStore(env.Options), workbench);

        var result = await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineSessionStatus.Ready, result.Status);
        Assert.Contains(result.Warnings, warning => warning.Contains("uia backend unavailable", StringComparison.Ordinal));
        Assert.Null(result.CurrentSlide);
        Assert.DoesNotContain("powerpoint_online_uia_observed", result.Actions);
    }

    [Fact]
    public async Task ProbeOnlineAddInAsync_ReturnsReady_WhenHostReachableAndTaskPaneVisible()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "Windows Operator PowerPoint", string.Empty, "Pane", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("5", "My Add-ins", string.Empty, "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            new FakeInputService(),
            new FakeAddInHostProbe(),
            uia,
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.ProbeOnlineAddInAsync(
            "ppt-session",
            new PowerPointOnlineAddInProbeRequest
            {
                Capture = true,
                Label = "addin-probe",
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.Ready, result.Status);
        Assert.True(result.HostReachable);
        Assert.Equal("https://localhost:3003/taskpane.html", result.TaskPaneUrl);
        Assert.True(result.TaskPaneReachable);
        Assert.Equal("https://localhost:3003/manifest.xml", result.ManifestUrl);
        Assert.True(result.ManifestReachable);
        Assert.Equal("manifest-id", result.ManifestId);
        Assert.Equal("1.2.3.4", result.ManifestVersion);
        Assert.Equal("Windows Operator PowerPoint", result.ManifestDisplayName);
        Assert.Equal("https://localhost:3003/taskpane.html", result.ManifestSourceLocation);
        Assert.True(result.TaskPaneVisible);
        Assert.True(result.CommandVisible);
        Assert.Equal("https://localhost:3003", result.AddInBaseUrl);
        Assert.Contains("addin_taskpane_probe_ok", result.Actions);
        Assert.Contains("addin_manifest_probe_ok", result.Actions);
        Assert.Contains("addin_host_probe_ok", result.Actions);
        Assert.Contains("addin_taskpane_visible", result.Actions);
        Assert.Contains("addin_command_visible", result.Actions);
        Assert.Contains("addin_probe_screenshot_requested", result.Actions);
        Assert.Single(result.Evidence);
        Assert.Equal(2, result.MatchedElements.Count);
    }

    [Fact]
    public async Task ProbeOnlineAddInAsync_ReturnsHostUnavailable_WhenHostProbeFails()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            new FakeInputService(),
            new FakeAddInHostProbe
            {
                Result = new PowerPointOnlineAddInHostProbeResult(
                    false,
                    "manifest: tls failure",
                    "https://localhost:3003/taskpane.html",
                    true,
                    "https://localhost:3003/manifest.xml",
                    false,
                    null,
                    null,
                    null,
                    null),
            },
            new FakeUiAutomationService
            {
                Elements =
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
            },
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.ProbeOnlineAddInAsync(
            "ppt-session",
            new PowerPointOnlineAddInProbeRequest { Capture = false },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.HostUnavailable, result.Status);
        Assert.False(result.HostReachable);
        Assert.True(result.TaskPaneReachable);
        Assert.False(result.ManifestReachable);
        Assert.Equal("https://localhost:3003/taskpane.html", result.TaskPaneUrl);
        Assert.Equal("https://localhost:3003/manifest.xml", result.ManifestUrl);
        Assert.False(result.TaskPaneVisible);
        Assert.False(result.CommandVisible);
        Assert.Contains("addin_taskpane_probe_ok", result.Actions);
        Assert.Contains("addin_manifest_probe_failed", result.Actions);
        Assert.Contains("addin_host_probe_failed", result.Actions);
        Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.PowerPointUnavailable, result.Errors[0].Code);
    }

    [Fact]
    public async Task ProbeOnlineAddInAsync_ReturnsBlockedActivation_WhenHostReachableButTaskPaneHidden()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "Insert Add-ins", string.Empty, "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            input,
            new FakeAddInHostProbe(),
            uia,
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.ProbeOnlineAddInAsync(
            "ppt-session",
            new PowerPointOnlineAddInProbeRequest { Capture = false },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.BlockedActivation, result.Status);
        Assert.True(result.HostReachable);
        Assert.True(result.TaskPaneReachable);
        Assert.True(result.ManifestReachable);
        Assert.False(result.TaskPaneVisible);
        Assert.True(result.CommandVisible);
        Assert.Null(input.LastClick);
        Assert.Contains("addin_taskpane_probe_ok", result.Actions);
        Assert.Contains("addin_manifest_probe_ok", result.Actions);
        Assert.Contains("addin_taskpane_not_visible", result.Actions);
        Assert.Contains("addin_command_visible", result.Actions);
        Assert.DoesNotContain("addin_activation_requested", result.Actions);
        Assert.Single(result.MatchedElements);
    }

    [Fact]
    public async Task ProbeOnlineAddInAsync_ActivatesAddIn_WhenCommandClickableAndTaskPaneAppears()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "My Add-ins", string.Empty, "Button", true, false, new WindowBounds(100, 200, 40, 20)),
                ],
                [
                    new UiElementRef("5", "My Add-ins", string.Empty, "Button", true, false, new WindowBounds(100, 200, 40, 20)),
                ],
                [
                    new UiElementRef("6", "Windows Operator PowerPoint", string.Empty, "Pane", true, false, new WindowBounds(500, 100, 300, 500)),
                    new UiElementRef("7", "My Add-ins", string.Empty, "Button", true, false, new WindowBounds(100, 200, 40, 20)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            input,
            new FakeAddInHostProbe(),
            uia,
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.ProbeOnlineAddInAsync(
            "ppt-session",
            new PowerPointOnlineAddInProbeRequest
            {
                Capture = false,
                ActivateIfNeeded = true,
                ActivationTimeoutSeconds = 1,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.Ready, result.Status);
        Assert.NotNull(input.LastClick);
        Assert.Equal(120, input.LastClick!.X);
        Assert.Equal(210, input.LastClick.Y);
        Assert.Contains("addin_activation_requested", result.Actions);
        Assert.Contains("addin_activation_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_observed_ready", result.Actions);
        Assert.DoesNotContain("addin_activation_timeout", result.Actions);
        Assert.True(result.TaskPaneReachable);
        Assert.True(result.ManifestReachable);
        Assert.True(result.TaskPaneVisible);
        Assert.True(result.CommandVisible);
        Assert.Contains(result.MatchedElements, element => element.Name == "Windows Operator PowerPoint");
        Assert.Contains(result.MatchedElements, element => element.Name == "My Add-ins");
    }

    [Fact]
    public async Task ProbeOnlineAddInAsync_ReturnsBlockedActivation_WhenActivationTimesOut()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "My Add-ins", string.Empty, "Button", true, false, new WindowBounds(100, 200, 40, 20)),
                ],
                [
                    new UiElementRef("5", "My Add-ins", string.Empty, "Button", true, false, new WindowBounds(100, 200, 40, 20)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            input,
            new FakeAddInHostProbe(),
            uia,
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.ProbeOnlineAddInAsync(
            "ppt-session",
            new PowerPointOnlineAddInProbeRequest
            {
                Capture = false,
                ActivateIfNeeded = true,
                ActivationTimeoutSeconds = 1,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.BlockedActivation, result.Status);
        Assert.NotNull(input.LastClick);
        Assert.Contains("addin_activation_requested", result.Actions);
        Assert.Contains("addin_activation_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_timeout", result.Actions);
        Assert.True(result.TaskPaneReachable);
        Assert.True(result.ManifestReachable);
        Assert.False(result.TaskPaneVisible);
    }

    [Fact]
    public async Task ProbeOnlineAddInAsync_RevealsOffscreenCommand_FromHomeTabBeforeActivationClick()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "My Add-ins", string.Empty, "Button", true, true, new WindowBounds(100, 200, 40, 20)),
                ],
                [
                    new UiElementRef("5", "Home", "HomeTab", "TabItem", true, false, new WindowBounds(20, 50, 60, 24)),
                    new UiElementRef("6", "My Add-ins", string.Empty, "Button", true, true, new WindowBounds(100, 200, 40, 20)),
                ],
                [
                    new UiElementRef("7", "My Add-ins", string.Empty, "Button", true, false, new WindowBounds(100, 200, 40, 20)),
                ],
                [
                    new UiElementRef("8", "Windows Operator PowerPoint", string.Empty, "Pane", true, false, new WindowBounds(500, 100, 300, 500)),
                    new UiElementRef("9", "My Add-ins", string.Empty, "Button", true, false, new WindowBounds(100, 200, 40, 20)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            input,
            new FakeAddInHostProbe(),
            uia,
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.ProbeOnlineAddInAsync(
            "ppt-session",
            new PowerPointOnlineAddInProbeRequest
            {
                Capture = false,
                ActivateIfNeeded = true,
                ActivationTimeoutSeconds = 1,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.Ready, result.Status);
        Assert.Equal(2, input.ClickHistory.Count);
        Assert.Equal(50, input.ClickHistory[0].X);
        Assert.Equal(62, input.ClickHistory[0].Y);
        Assert.Equal(120, input.ClickHistory[1].X);
        Assert.Equal(210, input.ClickHistory[1].Y);
        Assert.Contains("addin_activation_home_tab_click_dispatched", result.Actions);
        Assert.DoesNotContain("addin_activation_insert_tab_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_observed_ready", result.Actions);
        Assert.True(result.TaskPaneVisible);
    }

    [Fact]
    public async Task ProbeOnlineAddInAsync_ActivatesAfterReveal_WhenInitialCommandMissing()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponsePlan = new Queue<object>(
            [
                new UiElementRef[]
                {
                    new("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                },
                new UiElementRef[]
                {
                    new("4", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new("5", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new("6", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                },
                Array.Empty<UiElementRef>(),
                new UiElementRef[]
                {
                    new("7", "Home", "HomeTab", "TabItem", true, false, new WindowBounds(20, 50, 60, 24)),
                },
                new UiElementRef[]
                {
                    new("8", "Run Update", string.Empty, "Button", true, false, new WindowBounds(1080, 720, 160, 40)),
                },
                new UiElementRef[]
                {
                    new("9", "Windows Operator PowerPoint", string.Empty, "Pane", true, false, new WindowBounds(500, 100, 300, 500)),
                    new("10", "Run Update", string.Empty, "Button", true, false, new WindowBounds(1080, 720, 160, 40)),
                },
            ]),
        };
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            input,
            new FakeAddInHostProbe(),
            uia,
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.ProbeOnlineAddInAsync(
            "ppt-session",
            new PowerPointOnlineAddInProbeRequest
            {
                Capture = false,
                ActivateIfNeeded = true,
                ActivationTimeoutSeconds = 1,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.Ready, result.Status);
        Assert.Equal(2, input.ClickHistory.Count);
        Assert.Equal(50, input.ClickHistory[0].X);
        Assert.Equal(62, input.ClickHistory[0].Y);
        Assert.Equal(1160, input.ClickHistory[1].X);
        Assert.Equal(740, input.ClickHistory[1].Y);
        Assert.Contains("addin_command_not_visible", result.Actions);
        Assert.Contains("addin_activation_requested", result.Actions);
        Assert.Contains("addin_activation_home_tab_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_observed_ready", result.Actions);
        Assert.True(result.TaskPaneVisible);
        Assert.True(result.CommandVisible);
    }

    [Fact]
    public async Task ProbeOnlineAddInAsync_ActivatesAfterReveal_WhenInitialUiaQueryFails()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponsePlan = new Queue<object>(
            [
                new UiElementRef[]
                {
                    new("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                },
                new UiElementRef[]
                {
                    new("4", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new("5", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new("6", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                },
                new OperatorFailureException(OperatorErrors.PowerPointUnavailable("initial uia failure")),
                new UiElementRef[]
                {
                    new("7", "Home", "HomeTab", "TabItem", true, false, new WindowBounds(20, 50, 60, 24)),
                },
                new UiElementRef[]
                {
                    new("8", "Run Update", string.Empty, "MenuItem", true, false, new WindowBounds(1068, 736, 181, 33)),
                },
                new UiElementRef[]
                {
                    new("9", "Windows Operator PowerPoint", string.Empty, "Pane", true, false, new WindowBounds(500, 100, 300, 500)),
                    new("10", "Run Update", string.Empty, "MenuItem", true, false, new WindowBounds(1068, 736, 181, 33)),
                },
            ]),
        };
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            input,
            new FakeAddInHostProbe(),
            uia,
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.ProbeOnlineAddInAsync(
            "ppt-session",
            new PowerPointOnlineAddInProbeRequest
            {
                Capture = false,
                ActivateIfNeeded = true,
                ActivationTimeoutSeconds = 1,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.Ready, result.Status);
        Assert.Equal(2, input.ClickHistory.Count);
        Assert.Equal(50, input.ClickHistory[0].X);
        Assert.Equal(62, input.ClickHistory[0].Y);
        Assert.Equal(1158, input.ClickHistory[1].X);
        Assert.Equal(742, input.ClickHistory[1].Y);
        Assert.Contains("addin_activation_requested", result.Actions);
        Assert.Contains("addin_activation_home_tab_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_observed_ready", result.Actions);
        Assert.Contains(result.Warnings, warning => warning.Contains("initial uia failure", StringComparison.Ordinal));
        Assert.True(result.TaskPaneVisible);
        Assert.True(result.CommandVisible);
    }

    [Fact]
    public async Task ProbeOnlineAddInAsync_PrefersRunUpdateAfterOverflow_WhenHomeRevealsGenericAddIns()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("5", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("6", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("7", "Add-ins", "InsertAddInFlyout", "Button", true, true, new WindowBounds(100, 200, 40, 20)),
                ],
                [
                    new UiElementRef("8", "Home", "HomeTab", "TabItem", true, false, new WindowBounds(20, 50, 60, 24)),
                    new UiElementRef("9", "Add-ins", "InsertAddInFlyout", "Button", true, false, new WindowBounds(100, 200, 40, 20)),
                    new UiElementRef("10", "More Options", "RibbonOverflowMenu-overflow", "Button", true, false, new WindowBounds(1100, 90, 30, 30)),
                ],
                [
                    new UiElementRef("11", "Add-ins", "InsertAddInFlyout", "Button", true, false, new WindowBounds(100, 200, 40, 20)),
                    new UiElementRef("12", "More Options", "RibbonOverflowMenu-overflow", "Button", true, false, new WindowBounds(1100, 90, 30, 30)),
                ],
                [
                    new UiElementRef("13", "Updater", string.Empty, "Group", true, false, new WindowBounds(1040, 700, 220, 80)),
                    new UiElementRef("14", "Run Update", string.Empty, "MenuItem", true, false, new WindowBounds(1068, 736, 181, 33)),
                    new UiElementRef("15", "Add-ins", "InsertAddInFlyout", "Button", true, false, new WindowBounds(100, 200, 40, 20)),
                ],
                [
                    new UiElementRef("16", "Windows Operator PowerPoint", string.Empty, "Pane", true, false, new WindowBounds(500, 100, 300, 500)),
                    new UiElementRef("17", "Run Update", string.Empty, "MenuItem", true, false, new WindowBounds(1068, 736, 181, 33)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            input,
            new FakeAddInHostProbe(),
            uia,
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.ProbeOnlineAddInAsync(
            "ppt-session",
            new PowerPointOnlineAddInProbeRequest
            {
                Capture = false,
                ActivateIfNeeded = true,
                ActivationTimeoutSeconds = 1,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.Ready, result.Status);
        Assert.Equal(3, input.ClickHistory.Count);
        Assert.Equal(50, input.ClickHistory[0].X);
        Assert.Equal(62, input.ClickHistory[0].Y);
        Assert.Equal(1115, input.ClickHistory[1].X);
        Assert.Equal(105, input.ClickHistory[1].Y);
        Assert.Equal(1158, input.ClickHistory[2].X);
        Assert.Equal(742, input.ClickHistory[2].Y);
        Assert.Contains("addin_activation_home_tab_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_overflow_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_observed_ready", result.Actions);
        Assert.DoesNotContain("addin_activation_insert_tab_click_dispatched", result.Actions);
        Assert.DoesNotContain("addin_activation_timeout", result.Actions);
        Assert.True(result.TaskPaneVisible);
        Assert.Contains(result.MatchedElements, element => element.Name == "Run Update");
        Assert.Contains(result.MatchedElements, element => element.Name == "Windows Operator PowerPoint");
    }

    [Fact]
    public async Task ProbeOnlineAddInAsync_ToleratesTransientUiaFailureAfterActivationClick()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponsePlan = new Queue<object>(
            [
                new UiElementRef[]
                {
                    new("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                },
                new UiElementRef[]
                {
                    new("4", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new("5", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new("6", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                },
                new UiElementRef[]
                {
                    new("7", "Add-ins", "InsertAddInFlyout", "Button", true, true, new WindowBounds(100, 200, 40, 20)),
                },
                new UiElementRef[]
                {
                    new("8", "Home", "HomeTab", "TabItem", true, false, new WindowBounds(20, 50, 60, 24)),
                    new("9", "Add-ins", "InsertAddInFlyout", "Button", true, false, new WindowBounds(100, 200, 40, 20)),
                    new("10", "More Options", "RibbonOverflowMenu-overflow", "Button", true, false, new WindowBounds(1100, 90, 30, 30)),
                },
                new UiElementRef[]
                {
                    new("11", "Add-ins", "InsertAddInFlyout", "Button", true, false, new WindowBounds(100, 200, 40, 20)),
                    new("12", "More Options", "RibbonOverflowMenu-overflow", "Button", true, false, new WindowBounds(1100, 90, 30, 30)),
                },
                new UiElementRef[]
                {
                    new("13", "Updater", string.Empty, "Group", true, false, new WindowBounds(1040, 700, 220, 80)),
                    new("14", "Run Update", string.Empty, "MenuItem", true, false, new WindowBounds(1068, 736, 181, 33)),
                },
                new OperatorFailureException(OperatorErrors.PowerPointUnavailable("transient uia failure")),
                new UiElementRef[]
                {
                    new("15", "Windows Operator PowerPoint", string.Empty, "Pane", true, false, new WindowBounds(500, 100, 300, 500)),
                    new("16", "Run Pending Job", string.Empty, "Button", true, false, new WindowBounds(600, 200, 120, 40)),
                },
            ]),
        };
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            input,
            new FakeAddInHostProbe(),
            uia,
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.ProbeOnlineAddInAsync(
            "ppt-session",
            new PowerPointOnlineAddInProbeRequest
            {
                Capture = false,
                ActivateIfNeeded = true,
                ActivationTimeoutSeconds = 2,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.Ready, result.Status);
        Assert.Contains("addin_activation_observed_ready", result.Actions);
        Assert.DoesNotContain("addin_activation_timeout", result.Actions);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("addin_uia_unavailable:", StringComparison.Ordinal));
        Assert.True(result.TaskPaneVisible);
    }

    [Fact]
    public async Task ProbeOnlineAddInAsync_PreservesActivationCandidates_WhenRevealLosesCommand()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var input = new FakeInputService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("3", "Slide 4 of 71", string.Empty, "Text", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("4", "Add-ins", "InsertAddInFlyout", "Button", true, true, new WindowBounds(100, 200, 40, 20)),
                ],
                [
                    new UiElementRef("5", "Insert", "Insert", "TabItem", true, false, new WindowBounds(30, 50, 60, 24)),
                ],
                [
                    new UiElementRef("6", "More Options", "RibbonOverflowMenu-overflow", "Button", true, false, new WindowBounds(1100, 90, 30, 30)),
                ],
                [
                    new UiElementRef("7", "Links", string.Empty, "MenuItem", true, false, new WindowBounds(1000, 120, 120, 28)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            input,
            new FakeAddInHostProbe(),
            uia,
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.ProbeOnlineAddInAsync(
            "ppt-session",
            new PowerPointOnlineAddInProbeRequest
            {
                Capture = false,
                ActivateIfNeeded = true,
                ActivationTimeoutSeconds = 1,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.BlockedActivation, result.Status);
        Assert.False(result.CommandVisible);
        Assert.Equal(3, input.ClickHistory.Count);
        Assert.Equal(60, input.ClickHistory[0].X);
        Assert.Equal(62, input.ClickHistory[0].Y);
        Assert.Equal(1115, input.ClickHistory[1].X);
        Assert.Equal(105, input.ClickHistory[1].Y);
        Assert.Equal(120, input.ClickHistory[2].X);
        Assert.Equal(210, input.ClickHistory[2].Y);
        Assert.Contains("addin_activation_insert_tab_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_overflow_click_dispatched", result.Actions);
        Assert.DoesNotContain("addin_activation_home_tab_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_click_offscreen_candidate", result.Actions);
        Assert.Contains("addin_activation_click_dispatched", result.Actions);
        Assert.Contains("addin_activation_timeout", result.Actions);
        var element = Assert.Single(result.MatchedElements);
        Assert.Equal("Add-ins", element.Name);
        Assert.Equal("InsertAddInFlyout", element.AutomationId);
        Assert.True(element.IsOffscreen);
    }

    [Fact]
    public async Task ProbeOnlineAddInAsync_ReturnsBlockedSession_WhenSessionNotReady()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService
        {
            NextOpenState = FakeWorkbenchService.EdgeState(
                "ppt-session",
                "https://login.microsoftonline.com/",
                "Sign in to your account",
                "Stay signed in",
                true),
        };
        var probe = new FakeAddInHostProbe();
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService
            {
                SessionStateFactory = sessionId => FakeWorkbenchService.EdgeState(
                    sessionId,
                    "https://login.microsoftonline.com/",
                    "Sign in to your account",
                    "Stay signed in",
                    true),
            },
            new FakeInputService(),
            probe,
            new FakeUiAutomationService(),
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.ProbeOnlineAddInAsync(
            "ppt-session",
            new PowerPointOnlineAddInProbeRequest { Capture = false },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.BlockedSession, result.Status);
        Assert.False(result.HostReachable);
        Assert.Equal("https://localhost:3003/taskpane.html", result.TaskPaneUrl);
        Assert.False(result.TaskPaneReachable);
        Assert.Equal("https://localhost:3003/manifest.xml", result.ManifestUrl);
        Assert.False(result.ManifestReachable);
        Assert.Empty(result.MatchedElements);
        Assert.Equal(0, probe.CallCount);
        Assert.Equal(ErrorCodes.AuthUnavailable, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task WaitForOnlineSaveAsync_ReturnsImmediately_WhenAlreadySaved()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("3", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("4", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            new FakeInputService(),
            new FakeAddInHostProbe(),
            uia,
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.WaitForOnlineSaveAsync(
            "ppt-session",
            new PowerPointOnlineSaveWaitRequest
            {
                TimeoutSeconds = 5,
                PollSeconds = 1,
                Capture = false,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("saved", result.SaveState);
        Assert.Contains("save_wait_observed:saved", result.Actions);
        Assert.DoesNotContain("save_wait_timeout", result.Actions);
    }

    [Fact]
    public async Task WaitForOnlineSaveAsync_TimesOut_WhenSaveStateStaysSaving()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var uia = new FakeUiAutomationService
        {
            ResponseQueue = new Queue<IReadOnlyList<UiElementRef>>(
            [
                [
                    new UiElementRef("1", "Saved Click the cloud icon to view file location", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("2", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
                [
                    new UiElementRef("3", "Saving to cloud", "SaveStatusButton", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                    new UiElementRef("4", "Mode Menu;Editing Selected", "ModeSwitcher", "Button", true, false, new WindowBounds(0, 0, 10, 10)),
                ],
            ]),
        };
        var service = new PowerPointOnlineService(
            new FakeEdgeBrowserService(),
            new FakeInputService(),
            new FakeAddInHostProbe(),
            uia,
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.WaitForOnlineSaveAsync(
            "ppt-session",
            new PowerPointOnlineSaveWaitRequest
            {
                TimeoutSeconds = 0,
                PollSeconds = 1,
                Capture = false,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("saving", result.SaveState);
        Assert.Contains("save_wait_timeout", result.Actions);
        Assert.Contains("save_state_not_saved:saving", result.Warnings);
        Assert.Equal(ErrorCodes.PowerPointUnavailable, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task WaitForOnlineSaveAsync_Skips_WhenSessionNotReady()
    {
        using var env = new ExchangeRootScope();
        var workbench = new FakeWorkbenchService();
        var edge = new FakeEdgeBrowserService
        {
            SessionStateFactory = sessionId => FakeWorkbenchService.EdgeState(
                sessionId,
                "https://login.microsoftonline.com/",
                "Sign in to your account",
                "Stay signed in",
                true),
        };
        var service = new PowerPointOnlineService(
            edge,
            new FakeInputService(),
            new FakeAddInHostProbe(),
            new FakeUiAutomationService(),
            new WorkbenchRunStore(env.Options),
            workbench);

        await service.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = false,
            },
            CancellationToken.None);

        var result = await service.WaitForOnlineSaveAsync(
            "ppt-session",
            new PowerPointOnlineSaveWaitRequest(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineSessionStatus.BlockedAuth, result.Status);
        Assert.Contains("save_wait_skipped:BlockedAuth", result.Actions);
        Assert.Contains("session_not_ready:BlockedAuth", result.Warnings);
    }

    private sealed class ExchangeRootScope : IDisposable
    {
        public ExchangeRootScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "windows-operator-ppt-online-tests", Guid.NewGuid().ToString("N"));
            Options = Microsoft.Extensions.Options.Options.Create(
                new WorkbenchOptions
                {
                    ExchangeRoot = Root,
                    HostExchangeRoot = "/host-exchange",
                });
        }

        public string Root { get; }

        public IOptions<WorkbenchOptions> Options { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class FakeWorkbenchService : IWorkbenchService
    {
        public BrowserEdgeOpenUrlRequest? LastOpenRequest { get; private set; }

        public int OpenEdgeUrlCallCount { get; private set; }

        public BrowserEdgeSessionStateResult NextOpenState { get; set; } = EdgeState(
            "ppt-session",
            "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            "Deck - PowerPoint",
            "PowerPoint for the web",
            true);

        public BrowserEdgeSessionStateResult CleanupState { get; set; } = EdgeState(
            "ppt-session",
            "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            "Deck - PowerPoint",
            null,
            false);

        public bool CleanupThrows { get; init; }

        public Task<WindowRef> GetForegroundWindowAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DesktopScreenshotResult> CaptureDesktopScreenshotAsync(
            DesktopScreenshotRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BrowserEdgeOpenUrlResult> OpenEdgeUrlAsync(
            BrowserEdgeOpenUrlRequest request,
            CancellationToken cancellationToken)
        {
            OpenEdgeUrlCallCount++;
            LastOpenRequest = request;
            var state = NextOpenState with { SessionId = request.SessionId ?? NextOpenState.SessionId, Url = request.Url };
            return Task.FromResult(new BrowserEdgeOpenUrlResult(true, state, null, new[] { "session_started" }, Array.Empty<string>()));
        }

        public Task<DesktopScreenshotResult> CaptureEdgeSessionScreenshotAsync(
            string sessionId,
            DesktopScreenshotRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new DesktopScreenshotResult(
                    true,
                    new WorkbenchArtifactRef(
                        $@"Z:\operator-exchange\runs\ppt-session\screenshots\{request.Label}.png",
                        $"runs/ppt-session/screenshots/{request.Label}.png",
                        $"/host-exchange/runs/ppt-session/screenshots/{request.Label}.png",
                        "image/png",
                        3),
                    new WindowRef(
                        888,
                        777,
                        "Deck - PowerPoint",
                        "Chrome_WidgetWin_1",
                        new WindowBounds(-8, -8, 1296, 776),
                        1.0,
                        DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
                        true,
                        false),
                    1296,
                    776,
                    "Synthetic",
                    DateTimeOffset.Parse("2026-07-03T12:00:01Z"),
                    new[] { "artifact_written" },
                    Array.Empty<string>()));

        public Task<BrowserEdgeSessionStateResult> CleanupEdgeSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            if (CleanupThrows)
            {
                throw new OperatorFailureException(OperatorErrors.PowerPointUnavailable("missing browser"));
            }

            return Task.FromResult(CleanupState with { SessionId = sessionId });
        }

        public Task<WorkbenchSessionResult> GetSessionAsync(
            string sessionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DesktopScreenshotResult> CaptureSessionScreenshotAsync(
            string sessionId,
            DesktopScreenshotRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkbenchSessionCleanupResult> CleanupSessionAsync(
            string sessionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public static BrowserEdgeSessionStateResult EdgeState(
            string sessionId,
            string url,
            string? title,
            string? bodyText,
            bool isAlive) =>
            new(
                true,
                sessionId,
                BrowserEdgeProfileMode.Work,
                false,
                isAlive,
                new[] { isAlive ? "session_started" : "session_window_closed" },
                Array.Empty<string>(),
                DateTimeOffset.Parse("2026-07-03T12:00:02Z"),
                777,
                888,
                title,
                url,
                bodyText,
                Array.Empty<BrowserEdgeSessionElementRef>(),
                9222,
                isAlive ? "page_ready" : "session_closed",
                $@"C:\state\{sessionId}.json");
    }

    private sealed class FakeEdgeBrowserService : IEdgeBrowserService
    {
        public int DomClickCount { get; private set; }

        public BrowserEdgeSessionDomClickRequest? LastDomClickRequest { get; private set; }

        public BrowserEdgeSessionDomClickRequest? FirstDomClickRequest { get; private set; }

        public BrowserEdgeSessionNavigateRequest? LastNavigateRequest { get; private set; }

        public bool DomShouldFail { get; init; }

        public Func<string, BrowserEdgeSessionStateResult>? SessionStateFactory { get; init; }

        public Task<BrowserEdgeResetResult> ResetAsync(BrowserEdgeResetRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BrowserEdgeSessionStateResult> StartSessionAsync(BrowserEdgeSessionStartRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BrowserEdgeSessionStateResult> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(
                SessionStateFactory?.Invoke(sessionId) ??
                FakeWorkbenchService.EdgeState(
                    sessionId,
                    "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                    "Deck - PowerPoint",
                    "PowerPoint for the web",
                    true));

        public Task<BrowserEdgeSessionStateResult> NavigateSessionAsync(
            string sessionId,
            BrowserEdgeSessionNavigateRequest request,
            CancellationToken cancellationToken)
        {
            LastNavigateRequest = request;
            return Task.FromResult(
                FakeWorkbenchService.EdgeState(
                    sessionId,
                    request.Url ?? "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                    "Deck - PowerPoint",
                    "PowerPoint for the web",
                    true));
        }

        public Task<BrowserEdgeSessionDomActionResult> ClickDomAsync(
            string sessionId,
            BrowserEdgeSessionDomClickRequest request,
            CancellationToken cancellationToken)
        {
            DomClickCount++;
            LastDomClickRequest = request;
            FirstDomClickRequest ??= request;
            if (DomShouldFail)
            {
                return Task.FromResult(
                    new BrowserEdgeSessionDomActionResult(
                        false,
                        sessionId,
                        "click",
                        new[] { "click_requested" },
                        new[] { "No visible DOM match." },
                        DateTimeOffset.Parse("2026-07-03T12:00:03Z"),
                        null,
                        null,
                        null,
                        "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                        "Deck - PowerPoint",
                        string.Empty,
                        $@"C:\state\{sessionId}.json"));
            }

            return Task.FromResult(
                new BrowserEdgeSessionDomActionResult(
                    true,
                    sessionId,
                    "click",
                    new[] { "click_requested", "click_dispatched" },
                    Array.Empty<string>(),
                    DateTimeOffset.Parse("2026-07-03T12:00:03Z"),
                    request.Selector is null ? "visibleText" : "selector",
                    request.VisibleText ?? request.Selector,
                    "button",
                    "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                    "Deck - PowerPoint",
                    "PowerPoint for the web",
                    $@"C:\state\{sessionId}.json"));
        }

        public Task<BrowserEdgeSessionDomActionResult> FillDomAsync(
            string sessionId,
            BrowserEdgeSessionDomFillRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BrowserEdgeSessionStateResult> CloseSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeEdgeDevToolsService : IEdgeDevToolsService
    {
        private readonly EdgePageTarget? _target;
        private readonly EdgeDevToolsEvaluation _evaluation;

        public FakeEdgeDevToolsService(EdgePageTarget? target, EdgeDevToolsEvaluation evaluation)
        {
            _target = target;
            _evaluation = evaluation;
        }

        public List<string> Evaluations { get; } = [];

        public EdgePageTarget? ReadTarget(int devToolsPort, string? preferredUrl) => _target;

        public EdgeDevToolsEvaluation Evaluate(string webSocketDebuggerUrl, string expression, TimeSpan timeout)
        {
            Evaluations.Add(expression);
            return _evaluation;
        }

        public bool SendCommand(string webSocketDebuggerUrl, string method, object parameters, TimeSpan timeout) =>
            throw new NotSupportedException();

        public bool CloseTarget(int devToolsPort, string? targetId) =>
            throw new NotSupportedException();
    }

    private sealed class FakeInputService : IInputService
    {
        public ScreenClickRequest? LastClick { get; private set; }

        public List<ScreenClickRequest> ClickHistory { get; } = [];

        public List<IReadOnlyList<string>> HotkeyHistory { get; } = [];

        public ActionResult ClickResult { get; init; } = new(true, "clicked");

        public ActionResult HotkeyResult { get; init; } = new(true, "hotkey");

        public Task<ActionResult> ClickScreenAsync(ScreenClickRequest request, CancellationToken cancellationToken)
        {
            LastClick = request;
            ClickHistory.Add(request);
            return Task.FromResult(ClickResult with
            {
                Message = $"{ClickResult.Message}:{request.X},{request.Y}",
            });
        }

        public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken)
        {
            HotkeyHistory.Add(request.Keys);
            return Task.FromResult(HotkeyResult with
            {
                Message = $"{HotkeyResult.Message}:{string.Join("+", request.Keys)}",
            });
        }
    }

    private sealed class FakeUiAutomationService : IUiAutomationService
    {
        public IReadOnlyList<UiElementRef> Elements { get; init; } = Array.Empty<UiElementRef>();

        public Queue<IReadOnlyList<UiElementRef>>? ResponseQueue { get; init; }

        public Queue<object>? ResponsePlan { get; init; }

        public Func<UiQuery, IReadOnlyList<UiElementRef>>? QueryHandler { get; init; }

        public Exception? QueryException { get; init; }

        public Task<IReadOnlyList<UiElementRef>> QueryAsync(UiQuery query, CancellationToken cancellationToken)
        {
            if (QueryHandler is not null)
            {
                return Task.FromResult(QueryHandler(query));
            }

            if (ResponsePlan is { Count: > 0 })
            {
                var next = ResponsePlan.Dequeue();
                if (next is Exception exception)
                {
                    throw exception;
                }

                return Task.FromResult((IReadOnlyList<UiElementRef>)next);
            }

            if (QueryException is not null)
            {
                throw QueryException;
            }

            if (ResponseQueue is { Count: > 0 })
            {
                return Task.FromResult(ResponseQueue.Dequeue());
            }

            return Task.FromResult(Elements);
        }

        public Task<ActionResult> ClickAsync(UiaClickRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ActionResult> TypeAsync(UiaTypeRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAddInHostProbe : IPowerPointOnlineAddInHostProbe
    {
        public int CallCount { get; private set; }

        public PowerPointOnlineAddInHostProbeResult Result { get; init; } = new(
            true,
            null,
            "https://localhost:3003/taskpane.html",
            true,
            "https://localhost:3003/manifest.xml",
            true,
            "manifest-id",
            "1.2.3.4",
            "Windows Operator PowerPoint",
            "https://localhost:3003/taskpane.html");

        public Task<PowerPointOnlineAddInHostProbeResult> ProbeAsync(
            Uri addInBaseUri,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }
}
