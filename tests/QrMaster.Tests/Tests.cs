using System.Text.Json;
using QrMaster.Services;
using SkiaSharp;
using Xunit;

namespace QrMaster.Tests;

public class QrRoundTripTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("https://github.com/owner/repo?a=1&b=2")]
    [InlineData("Türkçe karakterler: çğıöşü € 日本語")]
    public void Generated_code_can_be_read_back(string text)
    {
        foreach (var ec in Enum.GetValues<EcLevel>())
        {
            var m = QrService.Encode(text, ec);
            using var bmp = QrService.Render(m, 400, 4, SKColors.Black, SKColors.White);
            Assert.Equal(text, Assert.Single(QrService.Decode(bmp)));
        }
    }

    [Fact]
    public void Transparent_png_round_trips()
    {
        var m = QrService.Encode("transparent", EcLevel.M);
        using var bmp = QrService.Render(m, 300, 4, SKColors.Black, SKColors.Transparent);
        var png = QrService.EncodeImage(bmp, ExportFormat.Png);
        using var decoded = SKBitmap.Decode(png);
        Assert.Equal("transparent", Assert.Single(QrService.Decode(decoded)));
    }

    [Theory]
    [InlineData(ExportFormat.Png)]
    [InlineData(ExportFormat.Jpeg)]
    [InlineData(ExportFormat.WebP)]
    [InlineData(ExportFormat.Bmp)]
    public void Exports_decode(ExportFormat f)
    {
        var m = QrService.Encode("export-test", EcLevel.M);
        using var bmp = QrService.Render(m, 400, 4, SKColors.Black, SKColors.White);
        using var back = SKBitmap.Decode(QrService.EncodeImage(bmp, f));
        Assert.Equal("export-test", Assert.Single(QrService.Decode(back)));
    }

    [Fact]
    public void Svg_is_well_formed()
    {
        var svg = QrService.ToSvg(QrService.Encode("x", EcLevel.L), 4, SKColors.Black, SKColors.White);
        System.Xml.Linq.XDocument.Parse(svg);
    }
}

public class SemVerTests
{
    [Fact]
    public void Precedence_chain()
    {
        string[] ascending = ["1.0.0-dev.2", "1.0.0-dev.10", "1.0.0-test.1", "1.0.0", "1.0.1-dev.1", "2.0.0"];
        for (var i = 0; i < ascending.Length - 1; i++)
        {
            Assert.True(SemVer.TryParse(ascending[i], out var lo));
            Assert.True(SemVer.TryParse(ascending[i + 1], out var hi));
            Assert.True(lo < hi, $"{lo} should be < {hi}");
        }
    }

    [Fact] public void Parses_v_prefix_and_build_metadata() => Assert.True(SemVer.TryParse("v1.2.3+abc", out var v) && v.ToString() == "1.2.3");
    [Fact] public void Rejects_garbage() => Assert.False(SemVer.TryParse("nightly", out _));
}
public class UpdateSelectionTests
{
    private const string Json = """
    [
      {"tag_name":"v1.2.0-dev.4","draft":false,"html_url":"u","assets":[]},
      {"tag_name":"v1.1.0-test.2","draft":false,"html_url":"u","assets":[]},
      {"tag_name":"v1.0.5","draft":false,"html_url":"u","body":"notes","assets":[
        {"name":"QrMaster-Setup-1.0.5-win-x64.exe","browser_download_url":"d","size":5,"digest":"sha256:ABC"},
        {"name":"QrMaster-Setup-1.0.5-win-arm64.exe","browser_download_url":"d2","size":5}]},
      {"tag_name":"v9.0.0","draft":true,"html_url":"u","assets":[]},
      {"tag_name":"nightly","draft":false,"html_url":"u","assets":[]}
    ]
    """;

    private static UpdateInfo? Pick(string current, UpdateChannel ch)
    {
        using var doc = JsonDocument.Parse(Json);
        SemVer.TryParse(current, out var c);
        return UpdateService.PickBest(doc.RootElement, c, ch);
    }

    [Fact] public void Prod_only_sees_stable() => Assert.Equal("1.0.5", Pick("1.0.0", UpdateChannel.Prod)!.Version);
    [Fact] public void Test_sees_test_and_stable() => Assert.Equal("1.1.0-test.2", Pick("1.0.0", UpdateChannel.Test)!.Version);
    [Fact] public void Dev_sees_everything() => Assert.Equal("1.2.0-dev.4", Pick("1.0.0", UpdateChannel.Dev)!.Version);
    [Fact] public void No_update_when_current_is_newest() => Assert.Null(Pick("1.0.5", UpdateChannel.Prod));
    [Fact] public void Drafts_are_ignored() => Assert.Null(Pick("1.2.0-dev.4", UpdateChannel.Dev));
    [Fact] public void Picks_installer_asset_with_digest() => Assert.Equal("ABC", Pick("1.0.0", UpdateChannel.Prod)!.Sha256);
}

