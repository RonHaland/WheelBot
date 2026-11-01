using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using WheelBotApiApp.Services;

namespace WheelBotApiApp.WheelGenerators;

public sealed class WheelGeneratorMultiPlatform : IWheelGenerator, IWheelBaseRenderer
{
    private const float MinAngleStep = 0.5f;
    private const int MaxAnimationFrames = 180;
    private const double AnimationFramerate = 24;
    private const int MaxFfmpegAngleSegments = 32;
    // Discord often ignores WebP loop metadata and keeps replaying attachments.
    // A long final-frame duration makes a forced loop look "stopped" on the result.
    private const int FinalFrameHoldMs = 60_000;

    private readonly Color[] _colors = [Color.Red, Color.Orange, Color.Yellow, Color.LimeGreen, Color.LightSkyBlue, Color.MediumPurple];
    private readonly Random _random = new();
    private readonly FontCollection _collection = new();
    private readonly int _size = 420;
    private readonly WheelBaseImageStore _baseImageStore;
    private readonly string _ffmpegPath;
    private readonly Lazy<byte[]> _pointerPngBytes;

    private DrawingOptions DrawingOptions => new()
    {
        GraphicsOptions = new GraphicsOptions
        {
            Antialias = true,
        },
    };

    public WheelGeneratorMultiPlatform(IConfiguration configuration, WheelBaseImageStore baseImageStore)
    {
        _baseImageStore = baseImageStore;
        _ffmpegPath = ResolveFfmpegPath(configuration);
        _pointerPngBytes = new Lazy<byte[]>(() =>
        {
            using var pointer = MakeSmallTriangle(10);
            using var stream = new MemoryStream();
            pointer.SaveAsPng(stream);
            return stream.ToArray();
        });
        var fontPath = configuration["FontPath"];
        if (Directory.Exists(fontPath))
        {
            if (File.Exists($"{fontPath}/Roboto-Regular.ttf"))
            {
                _collection.Add($"{fontPath}/Roboto-Regular.ttf");
            }
            if (File.Exists($"{fontPath}/NotoColorEmoji-Regular.ttf"))
            {
                _collection.Add($"{fontPath}/NotoColorEmoji-Regular.ttf");
            }
            if (File.Exists($"{fontPath}/Twemoji.Mozilla.ttf"))
            {
                _collection.Add($"{fontPath}/Twemoji.Mozilla.ttf");
            }
        }

        if (!_collection.Families.Any())
        {
            _collection.AddSystemFonts();
        }
    }

    public async Task<AnimatedWheel> GenerateAnimation(string wheelId, Wheel wheel)
    {
        var selectedIndex = _random.Next(wheel.Options.Count);
        var rotation = CalculateRotation(selectedIndex, wheel.Options.Count);
        var stream = await CreateAnimation(wheelId, wheel, 360 * 2 + rotation);

        return new AnimatedWheel(selectedIndex, wheel.Options[selectedIndex], stream);
    }

    static float CalculateRotation(int selectedIndex, int length)
    {
        var sweepAngle = 360f / length;
        var targetAngle = sweepAngle * selectedIndex + sweepAngle / 2;
        return 360 - targetAngle;
    }

    private static IPath MakeSlicePath(int r, float angle, float sweep)
    {
        var path = new PathBuilder();
        var center = new PointF(r, r);

        var startAngle = angle * Math.PI / 180d;
        var endDegrees = angle + sweep;
        var endAngle = endDegrees * Math.PI / 180d;

        var x1 = r * Math.Cos(startAngle) + center.X;
        var y1 = r * Math.Sin(startAngle) + center.Y;

        var x2 = r * Math.Cos(endAngle) + center.X;
        var y2 = r * Math.Sin(endAngle) + center.Y;

        var corner1 = new PointF((float)x1, (float)y1);
        var corner2 = new PointF((float)x2, (float)y2);

        path.MoveTo(center);
        path.LineTo(corner1);
        path.ArcTo(r, r, 0, false, true, corner2);
        path.CloseFigure();

        return path.Build();
    }

    private Image<Rgba32> MakeWheel(int r, Wheel wheel, float rotationDegrees = 0)
    {
        var numSlices = wheel.Options.Count;
        var sweepAngle = 360f / numSlices;
        var image = new Image<Rgba32>(r * 2, r * 2);
        var center = new PointF(r, r);
        var font = ResolvePrimaryFont().CreateFont(15, FontStyle.Regular);
        var emojiFallbacks = ResolveEmojiFallbackFonts();

        for (var i = numSlices - 1; i >= 0; i--)
        {
            var wheelEntryText = wheel.Options[i];
            var color = i == numSlices - 1 && numSlices == _colors.Length + 1 ? _colors[_colors.Length / 2] : _colors[i % _colors.Length];
            var startAngle = (rotationDegrees + sweepAngle * i) % 360;
            var path = MakeSlicePath(r, startAngle, sweepAngle);
            image.Mutate(o =>
            {
                o.Fill(DrawingOptions, color, path);
                o.Draw(DrawingOptions, new SolidPen(Color.Black, 2), path);
            });

            var textAngle = startAngle + sweepAngle / 2f;
            var textRadius = r * 0.6f;
            var textAngleRadians = (float)(Math.PI / 180f) * textAngle;
            var textPosition = new PointF(
                center.X + textRadius * (float)Math.Cos(textAngleRadians),
                center.Y + textRadius * (float)Math.Sin(textAngleRadians)
            );
            var textOpts = new RichTextOptions(font)
            {
                Origin = textPosition,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FallbackFontFamilies = emojiFallbacks,
            };
            var drawingOpts = new DrawingOptions
            {
                Transform = Matrix3x2.CreateRotation(textAngleRadians, textPosition),
                GraphicsOptions = DrawingOptions.GraphicsOptions
            };
            image.Mutate(o =>
            {
                o.Clip(path, ctx =>
                    ctx.DrawText(drawingOpts, textOpts, wheelEntryText, Brushes.Solid(Color.Black), null));
            });
        }

        return image;
    }

    public byte[] RenderBaseWheelPng(Wheel wheel)
    {
        using var png = new Image<Rgba32>(_size, _size, Color.Transparent);
        using var wheelImage = MakeWheel(_size / 2, wheel);
        png.Mutate(x => x.DrawImage(wheelImage, 1));
        using var stream = new MemoryStream();
        png.SaveAsPng(stream);
        return stream.ToArray();
    }

    public async Task<Stream> CreatePreview(string wheelId, Wheel wheel)
    {
        var hash = WheelContentHash.Compute(wheel.Options);
        var cached = await _baseImageStore.TryDownloadAsync(wheelId, hash);
        if (cached is not null)
        {
            return new MemoryStream(cached);
        }

        var pngBytes = RenderBaseWheelPng(wheel);
        await _baseImageStore.UploadAsync(wheelId, hash, pngBytes);
        return new MemoryStream(pngBytes);
    }

    private async Task<byte[]> LoadBaseWheelBytesAsync(string wheelId, Wheel wheel)
    {
        var hash = WheelContentHash.Compute(wheel.Options);
        var cached = await _baseImageStore.TryDownloadAsync(wheelId, hash);
        if (cached is null)
        {
            Console.WriteLine("Wheel base blob miss for {0}, rendering synchronously", wheelId);
            cached = RenderBaseWheelPng(wheel);
            await _baseImageStore.UploadAsync(wheelId, hash, cached);
        }
        else
        {
            Console.WriteLine("Wheel base blob hit for {0}", wheelId);
        }

        return cached;
    }

    private async Task<Stream> CreateAnimation(string wheelId, Wheel wheel, float rotation)
    {
        var sw = Stopwatch.StartNew();
        var baseWheelBytes = await LoadBaseWheelBytesAsync(wheelId, wheel);
        var angles = BuildAngleList(rotation);
        var durationSeconds = angles.Count / AnimationFramerate;
        Console.WriteLine("Base wheel ready at {0} ms, {1} frames, {2:F2}s duration", sw.ElapsedMilliseconds, angles.Count, durationSeconds);

        var webpStream = await EncodeSpinWebpViaFfmpegAsync(baseWheelBytes, _pointerPngBytes.Value, angles);
        Console.WriteLine("WebP encoded at {0} ms, size: {1}", sw.ElapsedMilliseconds, webpStream.Length);
        sw.Stop();
        webpStream.Position = 0;
        return webpStream;
    }

    private static List<float> BuildAngleList(float rotation)
    {
        var angles = new List<float>();
        for (var i = 180f; i < rotation && angles.Count < MaxAnimationFrames - 1; i += Math.Max(MinAngleStep, (float)(20 * Factor(i, rotation))))
        {
            angles.Add(i);
        }

        if (angles.Count == 0 || Math.Abs(angles[^1] - rotation) > 0.01f)
        {
            angles.Add(rotation);
        }

        if (angles.Count > MaxAnimationFrames)
        {
            var sampled = new List<float>(MaxAnimationFrames);
            for (var s = 0; s < MaxAnimationFrames - 1; s++)
            {
                var index = (int)Math.Round(s * (angles.Count - 1) / (double)(MaxAnimationFrames - 1));
                sampled.Add(angles[index]);
            }

            sampled.Add(rotation);
            return sampled;
        }

        return angles;
    }

    private static IReadOnlyList<float> SubsampleAnglesForFfmpeg(IReadOnlyList<float> angles)
    {
        if (angles.Count <= MaxFfmpegAngleSegments)
            return angles;

        var sampled = new List<float>(MaxFfmpegAngleSegments);
        for (var s = 0; s < MaxFfmpegAngleSegments; s++)
        {
            var index = (int)Math.Round(s * (angles.Count - 1) / (double)(MaxFfmpegAngleSegments - 1));
            sampled.Add(angles[index]);
        }

        return sampled;
    }

    private static string BuildFfmpegRotateAngleExpression(IReadOnlyList<float> keyframeAngles, double durationSeconds)
    {
        if (keyframeAngles.Count < 2)
            throw new InvalidOperationException("Spin animation requires at least two angle keyframes.");

        var inv = CultureInfo.InvariantCulture;
        var segmentCount = keyframeAngles.Count - 1;

        string Segment(int index)
        {
            var t0 = index / (double)segmentCount * durationSeconds;
            var t1 = (index + 1) / (double)segmentCount * durationSeconds;
            var a0 = keyframeAngles[index] * Math.PI / 180d;
            var a1 = keyframeAngles[index + 1] * Math.PI / 180d;
            return $"{a0.ToString(inv)}+({a1.ToString(inv)}-{a0.ToString(inv)})*((t-{t0.ToString(inv)})/({t1.ToString(inv)}-{t0.ToString(inv)}))";
        }

        var expression = Segment(segmentCount - 1);
        for (var index = segmentCount - 2; index >= 0; index--)
        {
            var tBreak = (index + 1) / (double)segmentCount * durationSeconds;
            expression = $"if(lt(t,{tBreak.ToString(inv)}),{Segment(index)},{expression})";
        }

        return expression;
    }

    private async Task<MemoryStream> EncodeSpinWebpViaFfmpegAsync(byte[] baseWheelPng, byte[] pointerPng, IReadOnlyList<float> angles)
    {
        var durationSeconds = angles.Count / AnimationFramerate;
        var keyframes = SubsampleAnglesForFfmpeg(angles);
        var rotateExpression = BuildFfmpegRotateAngleExpression(keyframes, durationSeconds);
        var duration = durationSeconds.ToString(CultureInfo.InvariantCulture);
        var framerate = AnimationFramerate.ToString(CultureInfo.InvariantCulture);
        var frameCount = angles.Count.ToString(CultureInfo.InvariantCulture);

        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wheelbot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var basePath = System.IO.Path.Combine(tempDir, "base.png");
        var pointerPath = System.IO.Path.Combine(tempDir, "pointer.png");
        var outputPath = System.IO.Path.Combine(tempDir, "spin.webp");

        try
        {
            await File.WriteAllBytesAsync(basePath, baseWheelPng);
            await File.WriteAllBytesAsync(pointerPath, pointerPng);

            var filterComplex = new StringBuilder();
            filterComplex.Append(CultureInfo.InvariantCulture, $"[0:v]format=rgba,fps={framerate},rotate=a='{rotateExpression}':ow={_size}:oh={_size}:c=none:fillcolor=black@0[rot];");
            filterComplex.Append("[1:v]format=rgba[p];[rot][p]overlay=0:0:format=auto[out]");

            var stderr = await RunFfmpegSpinEncodeAsync(
                basePath,
                pointerPath,
                outputPath,
                filterComplex.ToString(),
                duration,
                frameCount,
                encoder: "libwebp_anim");

            if (!File.Exists(outputPath) || !IsValidAnimatedWebp(await File.ReadAllBytesAsync(outputPath)))
            {
                Console.WriteLine("libwebp_anim failed or produced invalid output; retrying with libwebp: {0}", stderr.Trim());
                stderr = await RunFfmpegSpinEncodeAsync(
                    basePath,
                    pointerPath,
                    outputPath,
                    filterComplex.ToString(),
                    duration,
                    frameCount,
                    encoder: "libwebp");
            }

            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException($"ffmpeg WebP encode failed: {stderr.Trim()}");
            }

            var bytes = await File.ReadAllBytesAsync(outputPath);
            if (!IsValidAnimatedWebp(bytes))
            {
                throw new InvalidOperationException("ffmpeg produced output that is not a valid animated WebP.");
            }

            var stream = new MemoryStream(bytes);
            PatchAnimatedWebpPlayback(stream, loopCount: 1, lastFrameDurationMs: FinalFrameHoldMs);
            return stream;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private async Task<string> RunFfmpegSpinEncodeAsync(
        string basePath,
        string pointerPath,
        string outputPath,
        string filterComplex,
        string duration,
        string frameCount,
        string encoder)
    {
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-loop");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-t");
        psi.ArgumentList.Add(duration);
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(basePath);
        psi.ArgumentList.Add("-loop");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-t");
        psi.ArgumentList.Add(duration);
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(pointerPath);
        psi.ArgumentList.Add("-filter_complex");
        psi.ArgumentList.Add(filterComplex);
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("[out]");
        psi.ArgumentList.Add("-frames:v");
        psi.ArgumentList.Add(frameCount);
        psi.ArgumentList.Add("-an");
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add(encoder);
        psi.ArgumentList.Add("-loop");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-preset");
        psi.ArgumentList.Add("default");
        psi.ArgumentList.Add("-compression_level");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-q:v");
        psi.ArgumentList.Add("70");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("yuva420p");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("webp");
        psi.ArgumentList.Add(outputPath);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg for WebP spin encoding.");

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            Console.WriteLine("ffmpeg WebP encode failed with {0} ({1}): {2}", encoder, process.ExitCode, stderr.Trim());
        }

        return stderr;
    }

    /// <summary>
    /// Forces ANIM loop count and last ANMF duration. Loop 0 = infinite, 1 = play once.
    /// </summary>
    private static void PatchAnimatedWebpPlayback(MemoryStream stream, ushort loopCount, int lastFrameDurationMs)
    {
        byte[] rented = Array.Empty<byte>();
        Span<byte> data;
        if (stream.TryGetBuffer(out var segment) && segment.Array is not null)
        {
            data = segment.AsSpan(0, (int)stream.Length);
        }
        else
        {
            rented = stream.ToArray();
            data = rented;
        }

        var anim = data.IndexOf("ANIM"u8);
        if (anim >= 0 && anim + 14 <= data.Length)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(anim + 12, 2), loopCount);
        }

        var lastAnmf = -1;
        for (var i = 0; i <= data.Length - 4;)
        {
            var relative = data.Slice(i).IndexOf("ANMF"u8);
            if (relative < 0)
                break;

            lastAnmf = i + relative;
            if (lastAnmf + 8 > data.Length)
                break;

            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(lastAnmf + 4, 4));
            i = lastAnmf + 8 + (int)chunkSize;
            if ((chunkSize & 1) != 0)
                i++; // RIFF chunk padding
        }

        if (lastAnmf >= 0 && lastAnmf + 23 <= data.Length)
        {
            var duration = Math.Clamp(lastFrameDurationMs, 1, 0xFFFFFF);
            data[lastAnmf + 20] = (byte)duration;
            data[lastAnmf + 21] = (byte)(duration >> 8);
            data[lastAnmf + 22] = (byte)(duration >> 16);
        }

        if (rented.Length > 0)
        {
            stream.SetLength(0);
            stream.Write(rented);
        }

        stream.Position = 0;
    }

    private static bool IsValidAnimatedWebp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || data[0] != (byte)'R' || data[1] != (byte)'I')
            return false;

        return data.IndexOf("ANIM"u8) >= 0 && data.IndexOf("ANMF"u8) >= 0;
    }

    private FontFamily ResolvePrimaryFont()
    {
        if (_collection.TryGet("Roboto", out var roboto))
            return roboto;

        return _collection.Families.First();
    }

    private IReadOnlyList<FontFamily> ResolveEmojiFallbackFonts()
    {
        var fallbacks = new List<FontFamily>();
        if (_collection.TryGet("Twemoji Mozilla", out var twemoji))
            fallbacks.Add(twemoji);
        if (_collection.TryGet("Noto Color Emoji", out var noto))
            fallbacks.Add(noto);
        return fallbacks;
    }

    private static string ResolveFfmpegPath(IConfiguration configuration)
    {
        var configured = configuration["FfmpegPath"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        if (OperatingSystem.IsMacOS())
        {
            string[] candidates =
            [
                "/opt/homebrew/opt/ffmpeg-full/bin/ffmpeg",
                "/usr/local/opt/ffmpeg-full/bin/ffmpeg",
                "/opt/homebrew/bin/ffmpeg",
                "/usr/local/bin/ffmpeg",
            ];
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return "ffmpeg";
    }

    private static double Factor(float x, float fullRotation) => ((fullRotation - (1 / fullRotation) * float.Pow(x, 2)) / fullRotation);

    private Image<Rgba32> MakeSmallTriangle(int height)
    {
        var img = new Image<Rgba32>(_size, _size);

        var pen = new SolidPen(Color.Black, 2);
        var brush = new SolidBrush(Color.DarkGray);
        PointF[] points =
        [
            new(_size - 1, _size / 2f + height / 2f),
            new(_size - 1, _size / 2f - height / 2f),
            new(_size - height, _size / 2f)
        ];
        img.Mutate(i => i.FillPolygon(brush, points));
        img.Mutate(i => i.DrawPolygon(pen, points));

        return img;
    }
}
