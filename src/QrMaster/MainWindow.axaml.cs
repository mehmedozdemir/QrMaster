using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using QrMaster.Services;
using SkiaSharp;

namespace QrMaster;

public partial class MainWindow : Window
{
    private static readonly (ExportFormat Format, string Name, string Ext, string Mime)[] Formats =
    [
        (ExportFormat.Png, "PNG", ".png", "image/png"),
        (ExportFormat.Jpeg, "JPEG", ".jpg", "image/jpeg"),
        (ExportFormat.WebP, "WebP", ".webp", "image/webp"),
        (ExportFormat.Bmp, "BMP", ".bmp", "image/bmp"),
        (ExportFormat.Svg, "SVG", ".svg", "image/svg+xml"),
    ];

    private readonly AppSettings _settings = App.Settings;
    private readonly UpdateService _updates = new();
    private CancellationTokenSource? _renderCts;
    private UpdateInfo? _pendingUpdate;
    private QrMatrix? _matrix;
    private Bitmap? _previewBitmap;
    private Bitmap? _readBitmap;
    private bool _loading = true;
    private string? _singleLink;

    public MainWindow()
    {
        InitializeComponent();
        LoadSettingsIntoUi();
        SizeSlider.Value = 512;
        MarginSlider.Value = 4;
        WireEvents();
        _loading = false;

        VersionText.Text = App.T("VersionFmt", AppInfo.Version);
        SetStatus(App.T("Ready"));
        UpdateSliderLabels();
        UpdateWindowBackground();
        ActualThemeVariantChanged += (_, _) => UpdateWindowBackground();
        PropertyChanged += (_, e) => { if (e.Property == ActualTransparencyLevelProperty) UpdateWindowBackground(); };
        Opened += async (_, _) => await AutoCheckAsync();
    }

    private void LoadSettingsIntoUi()
    {
        ThemeBox.SelectedIndex = (int)_settings.Theme;
        LanguageBox.SelectedIndex = _settings.Language == "tr" ? 1 : 0;
        ChannelBox.SelectedIndex = (int)_settings.Channel;
        AutoCheckSwitch.IsChecked = _settings.AutoCheckUpdates;
        EcBox.SelectedIndex = (int)EcLevel.M;
    }

    private void WireEvents()
    {
        InputBox.TextChanged += (_, _) => ScheduleRender();
        EcBox.SelectionChanged += (_, _) => ScheduleRender();
        SizeSlider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { UpdateSliderLabels(); ScheduleRender(); } };
        MarginSlider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { UpdateSliderLabels(); ScheduleRender(); } };
        FgPicker.ColorChanged += (_, _) => ScheduleRender();
        BgPicker.ColorChanged += (_, _) => ScheduleRender();
        TransparentCheck.IsCheckedChanged += (_, _) => { BgPicker.IsEnabled = TransparentCheck.IsChecked != true; ScheduleRender(); };

        CopyImageButton.Click += async (_, _) => await CopyImageAsync();
        CopyTextButton.Click += async (_, _) => await CopyTextAsync(InputBox.Text);
        SaveButton.Click += async (_, _) => await SaveAsync();

        OpenButton.Click += async (_, _) => await OpenImageAsync();
        PasteButton.Click += async (_, _) => await PasteAsync();
        CopyResultButton.Click += async (_, _) => await CopyTextAsync(ResultBox.Text);
        OpenLinkButton.Click += async (_, _) => { if (_singleLink is not null) await LaunchAsync(_singleLink); };

        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control && Tabs.SelectedIndex == 1)
            {
                e.Handled = true;
                await PasteAsync();
            }
        };

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        AddHandler(DragDrop.DropEvent, OnDrop);

        ThemeBox.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.Theme = (ThemeChoice)Math.Max(0, ThemeBox.SelectedIndex);
            App.ApplyTheme(_settings.Theme);
            _settings.Save();
        };
        LanguageBox.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.Language = LanguageBox.SelectedIndex == 1 ? "tr" : "en";
            App.ApplyLanguage(_settings.Language);
            VersionText.Text = App.T("VersionFmt", AppInfo.Version);
            SetStatus(App.T("Ready"));
            _settings.Save();
        };
        ChannelBox.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.Channel = (UpdateChannel)Math.Max(0, ChannelBox.SelectedIndex);
            _settings.Save();
        };
        AutoCheckSwitch.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.AutoCheckUpdates = AutoCheckSwitch.IsChecked == true;
            _settings.Save();
        };
        CheckNowButton.Click += async (_, _) => await CheckUpdatesAsync(manual: true);
        InstallButton.Click += async (_, _) => await InstallUpdateAsync();
        ReleaseButton.Click += async (_, _) => { if (_pendingUpdate is not null) await LaunchAsync(_pendingUpdate.ReleaseUrl); };
        DismissButton.Click += (_, _) => UpdateBar.IsVisible = false;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void UpdateWindowBackground()
    {
        // Without Mica/Acrylic a transparent window would render black.
        if (ActualTransparencyLevel == WindowTransparencyLevel.None)
            Background = new SolidColorBrush(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
                ? Color.Parse("#202020") : Color.Parse("#F3F3F3"));
        else
            Background = Brushes.Transparent;
    }

    // ───────────── Generate ─────────────

    private void UpdateSliderLabels()
    {
        SizeText.Text = $"{(int)SizeSlider.Value} px";
        MarginText.Text = $"{(int)MarginSlider.Value} {App.T("Modules")}";
    }

    private SKColor Fg => ToSk(FgPicker.Color);
    private SKColor Bg => TransparentCheck.IsChecked == true ? SKColors.Transparent : ToSk(BgPicker.Color);
    private static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);

    private async void ScheduleRender()
    {
        if (_loading) return;
        _renderCts?.Cancel();
        var cts = _renderCts = new CancellationTokenSource();
        var text = InputBox.Text ?? "";
        var ec = (EcLevel)Math.Max(0, EcBox.SelectedIndex);
        var size = (int)SizeSlider.Value;
        var margin = (int)MarginSlider.Value;
        var fg = Fg;
        var bg = Bg;

        try
        {
            await Task.Delay(120, cts.Token);
            if (text.Length == 0)
            {
                ShowPreview(null, null, 0);
                return;
            }

            var (matrix, png) = await Task.Run(() =>
            {
                var m = QrService.Encode(text, ec);
                using var bmp = QrService.Render(m, Math.Min(size, 768), margin, fg, bg);
                return (m, QrService.EncodeImage(bmp, ExportFormat.Png));
            }, cts.Token);

            if (cts.IsCancellationRequested) return;
            using var ms = new MemoryStream(png);
            ShowPreview(matrix, new Bitmap(ms), matrix.Size);
            SetStatus(App.T("Ready"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is ZXing.WriterException or ArgumentException or InvalidOperationException)
        {
            ShowPreview(null, null, 0);
            SetStatus(App.T("TooLong"));
        }
    }

    private void ShowPreview(QrMatrix? matrix, Bitmap? bitmap, int modules)
    {
        _matrix = matrix;
        var old = _previewBitmap;
        _previewBitmap = bitmap;
        PreviewImage.Source = bitmap;
        old?.Dispose();
        EmptyText.IsVisible = bitmap is null;
        InfoText.Text = matrix is null ? "" : $"{modules} × {modules} {App.T("Modules")}";
        CheckerBg.Background = TransparentCheck.IsChecked == true ? new SolidColorBrush(Color.Parse("#40808080")) : null;
    }

    private byte[]? RenderExport(ExportFormat format)
    {
        if (_matrix is null) return null;
        if (format == ExportFormat.Svg)
            return System.Text.Encoding.UTF8.GetBytes(QrService.ToSvg(_matrix, (int)MarginSlider.Value, Fg, Bg));
        using var bmp = QrService.Render(_matrix, (int)SizeSlider.Value, (int)MarginSlider.Value, Fg, Bg);
        return QrService.EncodeImage(bmp, format);
    }

    private async Task CopyImageAsync()
    {
        var png = RenderExport(ExportFormat.Png);
        if (png is null || Clipboard is not { } cb) return;
        try
        {
            using var ms = new MemoryStream(png);
            using var bmp = new Bitmap(ms);
            await cb.SetValueAsync(DataFormat.Bitmap, bmp);
            SetStatus(App.T("Copied"));
        }
        catch { SetStatus(App.T("ClipboardFailed")); }
    }

    private async Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text) || Clipboard is not { } cb) return;
        try
        {
            await cb.SetTextAsync(text);
            SetStatus(App.T("Copied"));
        }
        catch { SetStatus(App.T("ClipboardFailed")); }
    }

    private async Task SaveAsync()
    {
        if (_matrix is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = App.T("SaveAs"),
            SuggestedFileName = "qrcode",
            DefaultExtension = "png",
            ShowOverwritePrompt = true,
            FileTypeChoices = Formats.Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = [$"*{f.Ext}"],
                MimeTypes = [f.Mime]
            }).ToList()
        });
        if (file is null) return;

        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        var fmt = Formats.FirstOrDefault(f => f.Ext == ext || (ext == ".jpeg" && f.Format == ExportFormat.Jpeg)).Name is null
            ? ExportFormat.Png
            : Formats.First(f => f.Ext == ext || (ext == ".jpeg" && f.Format == ExportFormat.Jpeg)).Format;

        try
        {
            var data = await Task.Run(() => RenderExport(fmt));
            if (data is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(data);
            SetStatus(App.T("Saved", file.Name));
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    // ───────────── Read ─────────────

    private async Task OpenImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = App.T("OpenImage"),
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.ico"] }
            ]
        });
        if (files.Count == 0) return;
        await using var s = await files[0].OpenReadAsync();
        await ReadFromStreamAsync(s);
    }

    private async Task PasteAsync()
    {
        if (Clipboard is not { } cb) return;
        try
        {
            if (await cb.TryGetBitmapAsync() is { } bmp)
            {
                using var ms = new MemoryStream();
                bmp.Save(ms);
                bmp.Dispose();
                ms.Position = 0;
                await ReadFromStreamAsync(ms);
                return;
            }
            if (await cb.TryGetFilesAsync() is { Length: > 0 } files && files[0] is IStorageFile file)
            {
                await using var s = await file.OpenReadAsync();
                await ReadFromStreamAsync(s);
                return;
            }
            SetStatus(App.T("ClipboardEmpty"));
        }
        catch { SetStatus(App.T("ClipboardFailed")); }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            Tabs.SelectedIndex = 1;
            if (e.DataTransfer.TryGetFiles() is { Length: > 0 } files && files[0] is IStorageFile file)
            {
                await using var s = await file.OpenReadAsync();
                await ReadFromStreamAsync(s);
            }
            else if (e.DataTransfer.TryGetBitmap() is { } bmp)
            {
                using var ms = new MemoryStream();
                bmp.Save(ms);
                ms.Position = 0;
                await ReadFromStreamAsync(ms);
            }
        }
        catch { SetStatus(App.T("ReadFailed")); }
    }

    private async Task ReadFromStreamAsync(Stream input)
    {
        var bytes = new MemoryStream();
        await input.CopyToAsync(bytes);
        var data = bytes.ToArray();

        string[] results;
        try
        {
            results = await Task.Run(() =>
            {
                using var sk = SKBitmap.Decode(data) ?? throw new InvalidDataException();
                return QrService.Decode(sk);
            });
        }
        catch
        {
            SetStatus(App.T("ReadFailed"));
            return;
        }

        var old = _readBitmap;
        using (var ms = new MemoryStream(data)) _readBitmap = new Bitmap(ms);
        ReadImage.Source = _readBitmap;
        old?.Dispose();
        DropHintText.IsVisible = false;

        if (results.Length == 0)
        {
            ResultBox.Text = "";
            ResultInfo.Text = App.T("NoQr");
            CopyResultButton.IsEnabled = false;
            OpenLinkButton.IsVisible = false;
            _singleLink = null;
            return;
        }

        ResultBox.Text = string.Join(Environment.NewLine + Environment.NewLine, results);
        ResultInfo.Text = results.Length > 1 ? App.T("ResultCount", results.Length) : "";
        CopyResultButton.IsEnabled = true;
        _singleLink = results.Length == 1 && Uri.TryCreate(results[0].Trim(), UriKind.Absolute, out var u)
                      && (u.Scheme is "http" or "https" or "mailto") ? u.AbsoluteUri : null;
        OpenLinkButton.IsVisible = _singleLink is not null;
        SetStatus(App.T("Ready"));
    }

    private async Task LaunchAsync(string url)
    {
        try { await Launcher.LaunchUriAsync(new Uri(url)); } catch { }
    }

    // ───────────── Updates ─────────────

    private async Task AutoCheckAsync()
    {
        if (!_settings.AutoCheckUpdates) return;
        if (_settings.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(12)) return;
        await Task.Delay(TimeSpan.FromSeconds(4));
        await CheckUpdatesAsync(manual: false);
    }

    private async Task CheckUpdatesAsync(bool manual)
    {
        if (!AppInfo.IsRepositoryConfigured)
        {
            if (manual) SetStatus(App.T("RepoNotConfigured"));
            return;
        }
        if (manual) SetStatus(App.T("Checking"));
        try
        {
            var info = await _updates.CheckAsync(_settings.Channel);
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settings.Save();

            if (info is null)
            {
                if (manual) SetStatus(App.T("UpToDate"));
                return;
            }
            _pendingUpdate = info;
            UpdateText.Text = App.T("UpdateAvailable", info.Version, info.Channel.ToString().ToLowerInvariant());
            InstallButton.IsVisible = _updates.CanInstall(info);
            UpdateProgress.IsVisible = false;
            UpdateBar.IsVisible = true;
        }
        catch
        {
            if (manual) SetStatus(App.T("CheckFailed"));
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is null) return;
        InstallButton.IsEnabled = false;
        UpdateProgress.IsVisible = true;
        UpdateProgress.Value = 0;
        try
        {
            var progress = new Progress<double>(p =>
            {
                UpdateProgress.Value = p * 100;
                UpdateText.Text = App.T("Downloading", (int)(p * 100));
            });
            var path = await _updates.DownloadAsync(_pendingUpdate, progress, CancellationToken.None);
            UpdateService.LaunchInstaller(path);
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateText.Text = App.T("UpdateFailed", ex.Message);
            UpdateProgress.IsVisible = false;
            InstallButton.IsEnabled = true;
        }
    }
}



