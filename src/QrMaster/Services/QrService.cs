using System.Globalization;
using System.Text;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace QrMaster.Services;

public enum EcLevel { L, M, Q, H }

public enum ExportFormat { Png, Jpeg, WebP, Bmp, Svg }

public sealed class QrMatrix(int size, bool[] modules)
{
    public int Size { get; } = size;
    public bool this[int x, int y] => modules[y * Size + x];
}

public static class QrService
{
    public static QrMatrix Encode(string text, EcLevel ec)
    {
        var level = ec switch
        {
            EcLevel.L => ErrorCorrectionLevel.L,
            EcLevel.Q => ErrorCorrectionLevel.Q,
            EcLevel.H => ErrorCorrectionLevel.H,
            _ => ErrorCorrectionLevel.M
        };
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.ERROR_CORRECTION] = level,
            [EncodeHintType.CHARACTER_SET] = "UTF-8",
            [EncodeHintType.MARGIN] = 0
        };
        var bm = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0, hints);
        var n = bm.Width;
        var data = new bool[n * bm.Height];
        for (var y = 0; y < bm.Height; y++)
            for (var x = 0; x < n; x++)
                data[y * n + x] = bm[x, y];
        return new QrMatrix(n, data);
    }

    /// <summary>Renders using whole-pixel modules so edges stay perfectly sharp.</summary>
    public static SKBitmap Render(QrMatrix m, int sizePx, int margin, SKColor fg, SKColor bg)
    {
        var total = m.Size + margin * 2;
        var module = Math.Max(1, sizePx / total);
        var dim = module * total;

        var bmp = new SKBitmap(new SKImageInfo(dim, dim, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(bg);
        using var paint = new SKPaint { Color = fg, IsAntialias = false, Style = SKPaintStyle.Fill };

        for (var y = 0; y < m.Size; y++)
        {
            var x = 0;
            while (x < m.Size)
            {
                if (!m[x, y]) { x++; continue; }
                var start = x;
                while (x < m.Size && m[x, y]) x++;
                canvas.DrawRect((start + margin) * module, (y + margin) * module, (x - start) * module, module, paint);
            }
        }
        return bmp;
    }

    public static string ToSvg(QrMatrix m, int margin, SKColor fg, SKColor bg)
    {
        var total = m.Size + margin * 2;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {total} {total}\" width=\"{total * 10}\" height=\"{total * 10}\" shape-rendering=\"crispEdges\">");
        if (bg.Alpha > 0)
            sb.Append(CultureInfo.InvariantCulture, $"<rect width=\"{total}\" height=\"{total}\" {Fill(bg)}/>");
        sb.Append("<path ").Append(Fill(fg)).Append(" d=\"");
        for (var y = 0; y < m.Size; y++)
        {
            var x = 0;
            while (x < m.Size)
            {
                if (!m[x, y]) { x++; continue; }
                var start = x;
                while (x < m.Size && m[x, y]) x++;
                sb.Append(CultureInfo.InvariantCulture, $"M{start + margin} {y + margin}h{x - start}v1h-{x - start}z");
            }
        }
        sb.Append("\"/></svg>");
        return sb.ToString();

        static string Fill(SKColor c) =>
            $"fill=\"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}\"" +
            (c.Alpha < 255 ? string.Create(CultureInfo.InvariantCulture, $" fill-opacity=\"{c.Alpha / 255.0:0.###}\"") : "");
    }

    public static byte[] EncodeImage(SKBitmap bmp, ExportFormat format, int quality = 92)
    {
        if (format == ExportFormat.Bmp) return EncodeBmp(bmp);

        // JPEG has no alpha channel: flatten onto white.
        using var src = format == ExportFormat.Jpeg ? Flatten(bmp) : null;
        using var image = SKImage.FromBitmap(src ?? bmp);
        var fmt = format switch
        {
            ExportFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            ExportFormat.WebP => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png
        };
        using var data = image.Encode(fmt, quality);
        return data.ToArray();
    }

    private static byte[] EncodeBmp(SKBitmap bmp)
    {
        using var flat = Flatten(bmp);
        var w = flat.Width;
        var h = flat.Height;
        var stride = w * 4;
        var pixels = flat.GetPixelSpan();
        using var ms = new MemoryStream(54 + stride * h);
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(54 + stride * h); bw.Write(0); bw.Write(54);
        bw.Write(40); bw.Write(w); bw.Write(-h); bw.Write((short)1); bw.Write((short)32);
        bw.Write(0); bw.Write(stride * h); bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
        bw.Write(pixels);
        return ms.ToArray();
    }

    private static SKBitmap Flatten(SKBitmap src)
    {
        var dst = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var c = new SKCanvas(dst);
        c.Clear(SKColors.White);
        c.DrawBitmap(src, 0, 0);
        return dst;
    }

    /// <summary>Decodes every QR code found in the image. Returns an empty array when none is found.</summary>
    public static string[] Decode(SKBitmap source)
    {
        var results = TryDecode(source);
        if (results.Length == 0 && Math.Max(source.Width, source.Height) > 1400)
        {
            // Large photos/screenshots: a downscaled copy often locates the finder patterns better.
            var scale = 1200f / Math.Max(source.Width, source.Height);
            using var small = new SKBitmap(new SKImageInfo(
                Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)),
                SKColorType.Bgra8888, SKAlphaType.Opaque));
            using (var c = new SKCanvas(small))
            {
                c.Clear(SKColors.White);
                using var img = SKImage.FromBitmap(source);
                using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium };
                c.DrawImage(img, new SKRect(0, 0, small.Width, small.Height), paint);
            }
            results = TryDecode(small);
        }
        return results;
    }

    private static string[] TryDecode(SKBitmap source)
    {
        using var flat = Flatten(source);
        var luminance = new RGBLuminanceSource(flat.GetPixelSpan().ToArray(), flat.Width, flat.Height,
            RGBLuminanceSource.BitmapFormat.BGRA32);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = [BarcodeFormat.QR_CODE]
            }
        };

        var found = reader.DecodeMultiple(luminance);
        if (found is { Length: > 0 })
            return found.Select(r => r.Text).Where(t => t is not null).Distinct().ToArray();

        var single = reader.Decode(luminance);
        return single?.Text is { } t ? [t] : [];
    }
}

