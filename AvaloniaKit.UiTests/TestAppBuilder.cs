using Avalonia;
using Avalonia.Headless;
using AvaloniaKit.UiTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace AvaloniaKit.UiTests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
