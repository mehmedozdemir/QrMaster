using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using QrMaster.Services;

namespace QrMaster;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Settings = AppSettings.Load();
        ApplyTheme(Settings.Theme);
        ApplyLanguage(Settings.Language);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(ThemeChoice t) =>
        Current!.RequestedThemeVariant = t switch
        {
            ThemeChoice.Light => ThemeVariant.Light,
            ThemeChoice.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

    public static void ApplyLanguage(string code)
    {
        var app = Current!;
        var file = code == "tr" ? "tr" : "en";
        var dict = new ResourceInclude(new Uri("avares://QrMaster/"))
        {
            Source = new Uri($"avares://QrMaster/Assets/Strings.{file}.axaml")
        };
        app.Resources.MergedDictionaries[0] = dict;
    }

    public static string T(string key) =>
        Current!.TryGetResource("S." + key, null, out var v) && v is string s ? s : key;

    public static string T(string key, params object[] args) => string.Format(T(key), args);
}

