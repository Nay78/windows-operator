using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Services;

public sealed class PowerPointComService : IPowerPointService, IDisposable
{
    private const int MsoFalse = 0;
    private const int MsoTrue = -1;
    private const int MsoGroup = 6;
    private const int PpSaveAsPdf = 32;
    private readonly StaComDispatcher _dispatcher = new();

    public Task<PowerPointInspectResult> InspectAsync(
        PowerPointInspectRequest request,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => InspectCore(request), cancellationToken);

    public Task<PowerPointEditResult> EditAsync(
        PowerPointEditRequest request,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => EditCore(request), cancellationToken);

    public Task<PowerPointEditResult> GetJobAsync(string jobId, CancellationToken cancellationToken) =>
        Task.FromResult(ReadJob(jobId));

    public void Dispose() => _dispatcher.Dispose();

    private static PowerPointInspectResult InspectCore(PowerPointInspectRequest request)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        try
        {
            return WithPresentation(
                ResolvePresentationSource(request.PresentationUrl, request.PresentationPath, request.ExchangePath, allowMissingFile: false),
                readOnly: true,
                allowMacroEnabled: false,
                body: session =>
                {
                    var slides = InspectSlides(session.Presentation, request.IncludeText, request.IncludeHidden);
                    return new PowerPointInspectResult(
                        true,
                        ReadPresentationRef(session.Presentation, session.Source),
                        slides,
                        warnings,
                        errors,
                        DateTimeOffset.UtcNow);
                });
        }
        catch (OperatorFailureException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw PowerPointUnavailable(ex);
        }
    }

    private static PowerPointEditResult EditCore(PowerPointEditRequest request)
    {
        ValidateMode(request.Mode);
        var jobId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6];
        var warnings = new List<string>();
        var errors = new List<string>();
        var outcomes = new List<PowerPointEditOutcome>();
        string? outputPath = null;

        try
        {
            var source = ResolvePresentationSource(request.PresentationUrl, request.PresentationPath, request.ExchangePath, allowMissingFile: false);
            return WithPresentation(
                source,
                readOnly: request.DryRun,
                request.AllowMacroEnabled,
                session =>
                {
                    foreach (var edit in request.Edits)
                    {
                        outcomes.Add(ApplyEdit(session.Presentation, edit, request.DryRun));
                    }

                    if (outcomes.Any(edit => edit.Errors.Count > 0))
                    {
                        errors.Add("One or more edits failed validation.");
                    }

                    if (!request.DryRun && errors.Count == 0)
                    {
                        outputPath = SavePresentation(session.Presentation, request, jobId);
                    }

                    WriteEditResult(jobId, source.Display, outputPath, request.DryRun, outcomes, warnings, errors);

                    return new PowerPointEditResult(
                        errors.Count == 0,
                        request.DryRun,
                        jobId,
                        source.Display,
                        outputPath,
                        outcomes,
                        warnings,
                        errors,
                        DateTimeOffset.UtcNow);
                });
        }
        catch (OperatorFailureException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw PowerPointUnavailable(ex);
        }
    }

    private static PowerPointEditOutcome ApplyEdit(dynamic presentation, PowerPointEditOperation edit, bool dryRun)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var op = edit.Op.Trim();

        try
        {
            if (string.IsNullOrWhiteSpace(edit.Id))
            {
                errors.Add("Edit id is required.");
            }

            if (string.IsNullOrWhiteSpace(op))
            {
                errors.Add("Edit op is required.");
            }

            if (errors.Count > 0)
            {
                return Outcome(edit, 0, 0, null, null, warnings, errors);
            }

            if (op.Equals("exportPdf", StringComparison.OrdinalIgnoreCase))
            {
                var pdfPath = ResolveOutputPath(edit.OutputPath, ".pdf", "powerpoint-export");
                if (!dryRun)
                {
                    presentation.SaveAs(pdfPath, PpSaveAsPdf);
                }

                return Outcome(edit, 1, dryRun ? 0 : 1, null, pdfPath, warnings, errors);
            }

            var target = ResolveTargets(presentation, edit);
            if (target.Errors.Count > 0)
            {
                return Outcome(edit, target.Matches.Count, 0, null, null, warnings, target.Errors);
            }

            var changed = 0;
            string? firstBefore = null;
            string? firstAfter = null;
            foreach (var match in target.Matches)
            {
                var result = ApplyShapeEdit(match.Slide, match.Shape, edit, dryRun);
                firstBefore ??= result.Before;
                firstAfter ??= result.After;
                if (result.Changed)
                {
                    changed++;
                }

                warnings.AddRange(result.Warnings);
                errors.AddRange(result.Errors);
            }

            return Outcome(edit, target.Matches.Count, changed, firstBefore, firstAfter, warnings, errors);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            return Outcome(edit, 0, 0, null, null, warnings, errors);
        }
    }

    private static ShapeEditResult ApplyShapeEdit(dynamic slide, dynamic shape, PowerPointEditOperation edit, bool dryRun)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var op = edit.Op.Trim();

        if (op.Equals("replaceText", StringComparison.OrdinalIgnoreCase))
        {
            var before = ReadShapeText(shape);
            if (before is null)
            {
                errors.Add($"Shape '{SafeName(shape)}' has no text.");
                return new(false, null, null, warnings, errors);
            }

            if (string.IsNullOrEmpty(edit.Find))
            {
                errors.Add("replaceText requires find.");
                return new(false, before, before, warnings, errors);
            }

            var after = before.Replace(edit.Find, edit.Value ?? "", StringComparison.Ordinal);
            if (!dryRun && after != before)
            {
                shape.TextFrame.TextRange.Text = after;
            }

            return new(after != before, before, after, warnings, errors);
        }

        if (op.Equals("setText", StringComparison.OrdinalIgnoreCase))
        {
            var before = ReadShapeText(shape);
            if (before is null)
            {
                errors.Add($"Shape '{SafeName(shape)}' has no text.");
                return new(false, null, null, warnings, errors);
            }

            var after = edit.Value ?? "";
            if (!dryRun && after != before)
            {
                shape.TextFrame.TextRange.Text = after;
            }

            return new(after != before, before, after, warnings, errors);
        }

        if (op.Equals("setTableCell", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsTrue(shape.HasTable))
            {
                errors.Add($"Shape '{SafeName(shape)}' is not a table.");
                return new(false, null, null, warnings, errors);
            }

            var row = edit.Row ?? 0;
            var column = edit.Column ?? 0;
            if (row < 1 || column < 1)
            {
                errors.Add("setTableCell requires one-based row and column.");
                return new(false, null, null, warnings, errors);
            }

            var cell = shape.Table.Cell(row, column).Shape.TextFrame.TextRange;
            var before = Convert.ToString(cell.Text, CultureInfo.InvariantCulture) ?? "";
            var after = edit.Value ?? "";
            if (!dryRun && after != before)
            {
                cell.Text = after;
            }

            return new(after != before, before, after, warnings, errors);
        }

        if (op.Equals("replaceImage", StringComparison.OrdinalIgnoreCase))
        {
            var imagePath = ResolveExistingAllowedFile(edit.ImagePath, "replaceImage requires imagePath.");
            var before = SafeName(shape);
            if (!dryRun)
            {
                ReplaceImage(slide, shape, imagePath);
            }

            return new(true, before, imagePath, warnings, errors);
        }

        if (op.Equals("setShapeVisible", StringComparison.OrdinalIgnoreCase))
        {
            if (edit.Visible is null)
            {
                errors.Add("setShapeVisible requires visible.");
                return new(false, null, null, warnings, errors);
            }

            var before = IsTrue(shape.Visible).ToString(CultureInfo.InvariantCulture);
            var after = edit.Visible.Value.ToString(CultureInfo.InvariantCulture);
            if (!dryRun && before != after)
            {
                shape.Visible = edit.Visible.Value ? MsoTrue : MsoFalse;
            }

            return new(before != after, before, after, warnings, errors);
        }

        if (op.Equals("setShapeFill", StringComparison.OrdinalIgnoreCase))
        {
            var color = ParseRgb(edit.FillColor);
            var before = Convert.ToString(shape.Fill.ForeColor.RGB, CultureInfo.InvariantCulture);
            var after = color.ToString(CultureInfo.InvariantCulture);
            if (!dryRun && before != after)
            {
                shape.Fill.ForeColor.RGB = color;
            }

            return new(before != after, before, edit.FillColor, warnings, errors);
        }

        errors.Add($"Unsupported PowerPoint edit op: {edit.Op}");
        return new(false, null, null, warnings, errors);
    }

    private static TargetResolution ResolveTargets(dynamic presentation, PowerPointEditOperation edit)
    {
        var errors = new List<string>();
        if (edit.Target?.Slide is null)
        {
            errors.Add("Edit target.slide is required.");
            return new(Array.Empty<ShapeMatch>(), errors);
        }

        if (edit.Target.Shape is null)
        {
            errors.Add("Edit target.shape is required.");
            return new(Array.Empty<ShapeMatch>(), errors);
        }

        var slides = FindSlides(presentation, edit.Target.Slide);
        if (slides.Count == 0)
        {
            errors.Add("No slide matched selector.");
            return new(Array.Empty<ShapeMatch>(), errors);
        }

        var matches = new List<ShapeMatch>();
        foreach (var slide in slides)
        {
            foreach (var shape in EnumerateShapes(slide, includeHidden: true))
            {
                if (ShapeMatches(shape.Shape, edit.Target.Shape))
                {
                    matches.Add(new ShapeMatch(slide, shape.Shape));
                }
            }
        }

        var assertion = edit.Assert ?? new PowerPointEditAssert { ExactlyOneTarget = true };
        if (matches.Count == 0)
        {
            errors.Add("No shape matched selector.");
        }
        else if ((assertion.ExactlyOneTarget || !assertion.AllowMultiple) && matches.Count != 1)
        {
            errors.Add($"Expected exactly one target, matched {matches.Count}.");
        }

        return new(matches, errors);
    }

    private static List<dynamic> FindSlides(dynamic presentation, PowerPointSlideSelector selector)
    {
        var matches = new List<dynamic>();
        var count = Convert.ToInt32(presentation.Slides.Count, CultureInfo.InvariantCulture);
        for (var i = 1; i <= count; i++)
        {
            dynamic slide = presentation.Slides[i];
            if (SlideMatches(slide, selector))
            {
                matches.Add(slide);
            }
        }

        return matches;
    }

    private static bool SlideMatches(dynamic slide, PowerPointSlideSelector selector)
    {
        if (selector.SlideId is not null &&
            Convert.ToInt32(slide.SlideID, CultureInfo.InvariantCulture) != selector.SlideId.Value)
        {
            return false;
        }

        if (selector.Index is not null &&
            Convert.ToInt32(slide.SlideIndex, CultureInfo.InvariantCulture) != selector.Index.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.Title) &&
            !string.Equals(ReadSlideTitle(slide), selector.Title, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (selector.Tag is not null && !TagsMatch(ReadTags(slide), selector.Tag))
        {
            return false;
        }

        return true;
    }

    private static bool ShapeMatches(dynamic shape, PowerPointShapeSelector selector)
    {
        if (selector.Id is not null &&
            Convert.ToInt32(shape.Id, CultureInfo.InvariantCulture) != selector.Id.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.Name) &&
            !string.Equals(SafeName(shape), selector.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (selector.Tag is not null && !TagsMatch(ReadTags(shape), selector.Tag))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.AltText) &&
            !string.Equals(SafeString(() => shape.AlternativeText), selector.AltText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.TextContains))
        {
            var text = ReadShapeText(shape);
            if (text is null || !text.Contains(selector.TextContains, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static PowerPointInspectResult WithPresentation(
        PresentationSource source,
        bool readOnly,
        bool allowMacroEnabled,
        Func<PowerPointSession, PowerPointInspectResult> body) =>
        WithPresentationCore(source, readOnly, allowMacroEnabled, body);

    private static PowerPointEditResult WithPresentation(
        PresentationSource source,
        bool readOnly,
        bool allowMacroEnabled,
        Func<PowerPointSession, PowerPointEditResult> body) =>
        WithPresentationCore(source, readOnly, allowMacroEnabled, body);

    private static T WithPresentationCore<T>(
        PresentationSource source,
        bool readOnly,
        bool allowMacroEnabled,
        Func<PowerPointSession, T> body)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointUnavailable("PowerPoint COM requires Windows."));
        }

        if (!allowMacroEnabled && source.IsMacroEnabled)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed("Macro-enabled PowerPoint files require allowMacroEnabled=true."));
        }

        using var mutex = new Mutex(initiallyOwned: false, @"Local\WindowsOperator.PowerPoint");
        if (!mutex.WaitOne(0))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointUnavailable("Another PowerPoint automation operation is already running."));
        }

        dynamic? app = null;
        dynamic? presentation = null;
        try
        {
            var type = Type.GetTypeFromProgID("PowerPoint.Application", throwOnError: false)
                ?? throw new OperatorFailureException(
                    OperatorErrors.PowerPointUnavailable("PowerPoint.Application COM ProgID not registered."));
            app = Activator.CreateInstance(type)
                ?? throw new OperatorFailureException(
                    OperatorErrors.PowerPointUnavailable("Unable to create PowerPoint.Application COM object."));
            app.Visible = MsoTrue;
            TrySet(() => app.DisplayAlerts = 1);
            presentation = app.Presentations.Open(source.OpenValue, readOnly ? MsoTrue : MsoFalse, MsoFalse, MsoTrue);
            return body(new PowerPointSession(app, presentation, source));
        }
        finally
        {
            TrySet(() => presentation?.Close());
            TrySet(() => app?.Quit());
            ReleaseCom(presentation);
            ReleaseCom(app);
            mutex.ReleaseMutex();
        }
    }

    private static PresentationSource ResolvePresentationSource(
        string? presentationUrl,
        string? presentationPath,
        string? exchangePath,
        bool allowMissingFile)
    {
        var values = new[] { presentationUrl, presentationPath, exchangePath }
            .Count(value => !string.IsNullOrWhiteSpace(value));
        if (values != 1)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed("Exactly one of presentationUrl, presentationPath, or exchangePath is required."));
        }

        if (!string.IsNullOrWhiteSpace(presentationUrl))
        {
            if (!Uri.TryCreate(presentationUrl.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new OperatorFailureException(
                    OperatorErrors.PowerPointValidationFailed($"Unsupported presentationUrl: {presentationUrl}"));
            }

            return new PresentationSource(uri.ToString(), uri.ToString(), IsMacroEnabled(uri.AbsolutePath));
        }

        var path = !string.IsNullOrWhiteSpace(exchangePath)
            ? ResolveExchangePath(exchangePath)
            : Path.GetFullPath(presentationPath!);
        if (!allowMissingFile && !File.Exists(path))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed($"Presentation file not found: {path}"));
        }

        return new PresentationSource(path, path, IsMacroEnabled(path));
    }

    private static string ResolveExchangePath(string raw)
    {
        var exchangeRoot = ExchangeRoot();
        var candidate = Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(exchangeRoot, raw));
        if (!IsUnder(candidate, exchangeRoot))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed($"Exchange path must be under {exchangeRoot}: {raw}"));
        }

        return candidate;
    }

    private static PowerPointPresentationRef ReadPresentationRef(dynamic presentation, PresentationSource source) =>
        new(
            SafeString(() => presentation.Name) ?? Path.GetFileName(source.Display),
            source.Display,
            Convert.ToInt32(presentation.Slides.Count, CultureInfo.InvariantCulture));

    private static IReadOnlyList<PowerPointSlideRef> InspectSlides(dynamic presentation, bool includeText, bool includeHidden)
    {
        var slides = new List<PowerPointSlideRef>();
        var count = Convert.ToInt32(presentation.Slides.Count, CultureInfo.InvariantCulture);
        for (var i = 1; i <= count; i++)
        {
            dynamic slide = presentation.Slides[i];
            var shapes = new List<PowerPointShapeRef>();
            foreach (var shape in EnumerateShapes(slide, includeHidden))
            {
                shapes.Add(ReadShapeRef(shape.Shape, shape.ParentName, shape.Level, includeText));
            }

            slides.Add(new PowerPointSlideRef(
                Convert.ToInt32(slide.SlideIndex, CultureInfo.InvariantCulture),
                Convert.ToInt32(slide.SlideID, CultureInfo.InvariantCulture),
                ReadSlideTitle(slide),
                ReadTags(slide),
                shapes));
        }

        return slides;
    }

    private static IEnumerable<ShapeNode> EnumerateShapes(dynamic slide, bool includeHidden)
    {
        var count = Convert.ToInt32(slide.Shapes.Count, CultureInfo.InvariantCulture);
        for (var i = 1; i <= count; i++)
        {
            foreach (var node in EnumerateShape(slide.Shapes[i], null, 0, includeHidden))
            {
                yield return node;
            }
        }
    }

    private static IEnumerable<ShapeNode> EnumerateShape(dynamic shape, string? parentName, int level, bool includeHidden)
    {
        if (includeHidden || IsTrue(shape.Visible))
        {
            yield return new ShapeNode(shape, parentName, level);
        }

        if (Convert.ToInt32(shape.Type, CultureInfo.InvariantCulture) != MsoGroup)
        {
            yield break;
        }

        var count = Convert.ToInt32(shape.GroupItems.Count, CultureInfo.InvariantCulture);
        for (var i = 1; i <= count; i++)
        {
            foreach (var child in EnumerateShape(shape.GroupItems[i], SafeName(shape), level + 1, includeHidden))
            {
                yield return child;
            }
        }
    }

    private static PowerPointShapeRef ReadShapeRef(dynamic shape, string? parentName, int level, bool includeText) =>
        new(
            Convert.ToInt32(shape.Id, CultureInfo.InvariantCulture),
            SafeName(shape),
            Convert.ToString(shape.Type, CultureInfo.InvariantCulture) ?? "Unknown",
            parentName,
            level,
            ReadTags(shape),
            SafeString(() => shape.AlternativeText),
            HasText(shape),
            IsTrue(SafeValue(() => shape.HasTable)),
            IsTrue(SafeValue(() => shape.HasChart)),
            IsTrue(shape.Visible),
            includeText ? ReadShapeText(shape) : null);

    private static IReadOnlyDictionary<string, string> ReadTags(dynamic obj)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var tags = obj.Tags;
            var count = Convert.ToInt32(tags.Count, CultureInfo.InvariantCulture);
            for (var i = 1; i <= count; i++)
            {
                result[Convert.ToString(tags.Name(i), CultureInfo.InvariantCulture) ?? ""] =
                    Convert.ToString(tags.Value(i), CultureInfo.InvariantCulture) ?? "";
            }
        }
        catch
        {
            return result;
        }

        return result;
    }

    private static bool TagsMatch(IReadOnlyDictionary<string, string> actual, IReadOnlyDictionary<string, string> expected)
    {
        foreach (var (key, value) in expected)
        {
            if (!actual.TryGetValue(key, out var actualValue) ||
                !string.Equals(actualValue, value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadSlideTitle(dynamic slide)
    {
        try
        {
            if (!IsTrue(slide.Shapes.HasTitle))
            {
                return null;
            }

            return Convert.ToString(slide.Shapes.Title.TextFrame.TextRange.Text, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static bool HasText(dynamic shape) =>
        IsTrue(SafeValue(() => shape.HasTextFrame)) && IsTrue(SafeValue(() => shape.TextFrame.HasText));

    private static string? ReadShapeText(dynamic shape)
    {
        try
        {
            return HasText(shape)
                ? Convert.ToString(shape.TextFrame.TextRange.Text, CultureInfo.InvariantCulture)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void ReplaceImage(dynamic slide, dynamic oldShape, string imagePath)
    {
        var name = SafeName(oldShape);
        var tags = ReadTags((object)oldShape);
        var altText = SafeString(() => oldShape.AlternativeText);
        var left = Convert.ToSingle(oldShape.Left, CultureInfo.InvariantCulture);
        var top = Convert.ToSingle(oldShape.Top, CultureInfo.InvariantCulture);
        var width = Convert.ToSingle(oldShape.Width, CultureInfo.InvariantCulture);
        var height = Convert.ToSingle(oldShape.Height, CultureInfo.InvariantCulture);
        oldShape.Delete();
        dynamic newShape = slide.Shapes.AddPicture(imagePath, MsoFalse, MsoTrue, left, top, width, height);
        newShape.Name = name;
        if (!string.IsNullOrWhiteSpace(altText))
        {
            newShape.AlternativeText = altText;
        }

        foreach (var pair in tags)
        {
            var key = pair.Key;
            var value = pair.Value;
            TrySet(() => newShape.Tags.Add(key, value));
        }
    }

    private static string SavePresentation(dynamic presentation, PowerPointEditRequest request, string jobId)
    {
        var mode = string.IsNullOrWhiteSpace(request.SaveMode) ? "overwrite" : request.SaveMode.Trim();
        if (mode.Equals("overwrite", StringComparison.OrdinalIgnoreCase))
        {
            presentation.Save();
            return SafeString(() => presentation.FullName) ?? request.PresentationPath ?? request.PresentationUrl ?? request.ExchangePath ?? "";
        }

        if (mode.Equals("copy", StringComparison.OrdinalIgnoreCase))
        {
            var outputPath = ResolveOutputPath(request.OutputPath, ".pptx", $"powerpoint-{jobId}");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            presentation.SaveCopyAs(outputPath);
            return outputPath;
        }

        if (mode.Equals("exportPdfOnly", StringComparison.OrdinalIgnoreCase))
        {
            var outputPath = ResolveOutputPath(request.OutputPath, ".pdf", $"powerpoint-{jobId}");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            presentation.SaveAs(outputPath, PpSaveAsPdf);
            return outputPath;
        }

        throw new OperatorFailureException(
            OperatorErrors.PowerPointValidationFailed($"Unsupported PowerPoint saveMode: {request.SaveMode}"));
    }

    private static string ResolveExistingAllowedFile(string? path, string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed(missingMessage));
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed($"File not found: {fullPath}"));
        }

        if (!IsUnder(fullPath, ExchangeRoot()) && !IsUnder(fullPath, LocalStateRoot()))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed($"PowerPoint file input must be under exchange or local state: {fullPath}"));
        }

        return fullPath;
    }

    private static string ResolveOutputPath(string? path, string extension, string baseName)
    {
        var fullPath = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(LocalStateRoot(), "run", "powerpoint", "outputs", baseName + extension)
            : Path.GetFullPath(path);

        if (!fullPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            fullPath += extension;
        }

        if (!IsUnder(fullPath, ExchangeRoot()) && !IsUnder(fullPath, LocalStateRoot()))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed($"PowerPoint output must be under exchange or local state: {fullPath}"));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return fullPath;
    }

    private static int ParseRgb(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith('#') || raw.Length != 7)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed("setShapeFill requires fillColor as #RRGGBB."));
        }

        var r = int.Parse(raw[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = int.Parse(raw[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = int.Parse(raw[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return r | (g << 8) | (b << 16);
    }

    private static void ValidateMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode) ||
            mode.Equals("powerpointDesktopCom", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new OperatorFailureException(
            OperatorErrors.PowerPointValidationFailed($"Unsupported PowerPoint mode: {mode}"));
    }

    private static bool IsMacroEnabled(string pathOrUrl) =>
        pathOrUrl.EndsWith(".pptm", StringComparison.OrdinalIgnoreCase) ||
        pathOrUrl.EndsWith(".ppsm", StringComparison.OrdinalIgnoreCase);

    private static string ExchangeRoot() =>
        Path.GetFullPath(Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_EXCHANGE_ROOT") ?? @"Z:\operator-exchange");

    private static string LocalStateRoot()
    {
        var configured = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(local)
            ? Path.Combine(Path.GetTempPath(), "WindowsOperator")
            : Path.Combine(local, "WindowsOperator"));
    }

    private static bool IsUnder(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrue(object? value) =>
        value switch
        {
            bool boolValue => boolValue,
            int intValue => intValue != 0,
            short shortValue => shortValue != 0,
            null => false,
            _ => Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0,
        };

    private static object? SafeValue(Func<object?> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeString(Func<object?> read)
    {
        try
        {
            return Convert.ToString(read(), CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static string SafeName(dynamic shape) =>
        SafeString(() => shape.Name) ?? "<unnamed>";

    private static void TrySet(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Best effort for PowerPoint process cleanup and metadata preservation.
        }
    }

    private static void ReleaseCom(object? obj)
    {
        if (obj is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(obj))
            {
                Marshal.FinalReleaseComObject(obj);
            }
        }
        catch
        {
            // Cleanup best effort.
        }
    }

    private static OperatorFailureException PowerPointUnavailable(Exception ex) =>
        ex is OperatorFailureException failure
            ? failure
            : new OperatorFailureException(OperatorErrors.PowerPointUnavailable(ex.Message));

    private static PowerPointEditOutcome Outcome(
        PowerPointEditOperation edit,
        int matched,
        int changed,
        string? before,
        string? after,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors) =>
        new(edit.Id, edit.Op, matched, changed, before, after, warnings, errors);

    private static void WriteEditResult(
        string jobId,
        string? presentationPath,
        string? outputPath,
        bool dryRun,
        IReadOnlyList<PowerPointEditOutcome> edits,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
    {
        var path = Path.Combine(LocalStateRoot(), "run", "powerpoint", "jobs", jobId + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var result = new PowerPointEditResult(
            errors.Count == 0,
            dryRun,
            jobId,
            presentationPath,
            outputPath,
            edits,
            warnings,
            errors,
            DateTimeOffset.UtcNow);
        File.WriteAllText(path, JsonSerializer.Serialize(result, OperatorJson.SerializerOptions));
    }

    private static PowerPointEditResult ReadJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId) ||
            jobId.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointJobNotFound(jobId));
        }

        var path = Path.Combine(LocalStateRoot(), "run", "powerpoint", "jobs", jobId + ".json");
        if (!File.Exists(path))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointJobNotFound(jobId));
        }

        return JsonSerializer.Deserialize<PowerPointEditResult>(File.ReadAllText(path), OperatorJson.SerializerOptions)
            ?? throw new OperatorFailureException(
                OperatorErrors.PowerPointJobNotFound(jobId));
    }

    private sealed record PresentationSource(string OpenValue, string Display, bool IsMacroEnabled);

    private sealed record PowerPointSession(dynamic Application, dynamic Presentation, PresentationSource Source);

    private sealed record ShapeNode(dynamic Shape, string? ParentName, int Level);

    private sealed record ShapeMatch(dynamic Slide, dynamic Shape);

    private sealed record TargetResolution(IReadOnlyList<ShapeMatch> Matches, IReadOnlyList<string> Errors);

    private sealed record ShapeEditResult(
        bool Changed,
        string? Before,
        string? After,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Errors);
}
