using Microsoft.Extensions.DependencyInjection;
using WindowsOperator.Automation.Services;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Automation.DependencyInjection;

public static class AutomationServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsAutomation(this IServiceCollection services)
    {
        services.AddSingleton<IWindowCatalogService, Win32WindowCatalogService>();
        services.AddSingleton<IWindowActivationService, Win32WindowActivationService>();
        services.AddSingleton<IUiAutomationBackend, FlaUiUia3AutomationBackend>();
        services.AddSingleton<IUiAutomationService, UiAutomationService>();
        services.AddSingleton<IInputService, HotkeyInputService>();
        return services;
    }
}
