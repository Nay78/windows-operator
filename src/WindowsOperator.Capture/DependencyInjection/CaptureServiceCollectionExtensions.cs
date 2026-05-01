using Microsoft.Extensions.DependencyInjection;
using WindowsOperator.Capture.Services;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Capture.DependencyInjection;

public static class CaptureServiceCollectionExtensions
{
    public static IServiceCollection AddWindowCapture(this IServiceCollection services)
    {
        services.AddSingleton<ImageEncodingService>();
        services.AddSingleton<ICaptureBackend, WindowsGraphicsCaptureBackend>();
        services.AddSingleton<ICaptureBackend, PrintWindowCaptureBackend>();
        services.AddSingleton<ICaptureBackend, GdiBitBltCaptureBackend>();
        services.AddSingleton<IScreenshotService, WindowCaptureService>();
        return services;
    }
}
