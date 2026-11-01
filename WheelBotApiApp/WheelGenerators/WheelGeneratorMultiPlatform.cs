using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Numerics;
using WheelBotApiApp.Services;

namespace WheelBotApiApp.WheelGenerators;

public sealed class WheelGeneratorMultiPlatform : IWheelGenerator, IWheelBaseRenderer
{
    private const float MinAngleStep = 0.5f;
    private const int MaxAnimationFrames = 180;
    private const double AnimationFramerate = 24;

    private readonly Color[] _colors = [Color.Red, Color.Orange, Color.Yellow, Color.LimeGreen, Color.LightSkyBlue, Color.MediumPurple];
    private readonly Random _random = new();
    private readonly FontCollection _collection = new();
    private readonly int _size = 420;
    private readonly WheelBaseImageStore _baseImageStore;
    private readonly string _ffmpegPath;

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
        var pointerBytes = RenderPointerPngBytes();
        var angles = BuildAngleList(rotation);
        Console.WriteLine("Base wheel ready at {0} ms, {1} frames", sw.ElapsedMilliseconds, angles.Count);

        var frameBytes = new byte[angles.Count][];
        var center = new PointF(_size / 2f, _size / 2f);
        Parallel.For(0, angles.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            using var baseWheel = Image.Load<Rgba32>(baseWheelBytes);
            using var pointer = Image.Load<Rgba32>(pointerBytes);
            using var frame = RenderRotatedFrame(baseWheel, pointer, angles[i], center);
            using var ms = new MemoryStream();
            frame.SaveAsPng(ms);
            frameBytes[i] = ms.ToArray();
        });

        Console.WriteLine("All frames generated at {0} ms", sw.ElapsedMilliseconds);
        var webpStream = await EncodeWebpAsync(frameBytes, _ffmpegPath);
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

    private Image<Rgba32> RenderRotatedFrame(Image<Rgba32> baseWheel, Image<Rgba32> pointer, float degrees, PointF center)
    {
        var frame = new Image<Rgba32>(_size, _size, Color.Transparent);
        using var rotatedWheel = baseWheel.Clone(ctx => ctx.Rotate(degrees));
        var offsetX = (int)(center.X - rotatedWheel.Width / 2f);
        var offsetY = (int)(center.Y - rotatedWheel.Height / 2f);
        frame.Mutate(ctx =>
        {
            ctx.DrawImage(rotatedWheel, new Point(offsetX, offsetY), 1f);
            ctx.DrawImage(pointer, new Point(0, 0), 1f);
        });
        return frame;
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
            ];
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return "ffmpeg";
    }

    private static async Task<MemoryStream> EncodeWebpAsync(byte[][] frameBytes, string ffmpegPath)
    {
        var ffmpegResult = await TryEncodeWebpViaFfmpegAsync(frameBytes, ffmpegPath);
        if (ffmpegResult is not null && IsValidAnimatedWebp(ffmpegResult.ToArray()))
        {
            return ffmpegResult;
        }

        ffmpegResult?.Dispose();
        Console.WriteLine("ffmpeg WebP output invalid or failed; falling back to img2webp");

        return await EncodeWebpViaImg2WebpAsync(frameBytes);
    }

    private static bool IsValidAnimatedWebp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || data[0] != (byte)'R' || data[1] != (byte)'I')
            return false;

        return data.IndexOf("ANIM"u8) >= 0 && data.IndexOf("ANMF"u8) >= 0;
    }

    private static async Task<MemoryStream?> TryEncodeWebpViaFfmpegAsync(byte[][] frameBytes, string ffmpegPath)
    {
        var outputStream = new MemoryStream();
        var framerate = AnimationFramerate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments =
                $"-hide_banner -loglevel error -y -f image2pipe -framerate {framerate} -vcodec png -i - " +
                $"-frames:v {frameBytes.Length} -c:v libwebp_anim -loop 0 -preset picture -q:v 75 -f webp pipe:1",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return null;

        var writeTask = WritePngFramesToStdinAsync(process, frameBytes);
        var readTask = process.StandardOutput.BaseStream.CopyToAsync(outputStream);
        var stderrTask = process.StandardError.ReadToEndAsync();

        await writeTask;
        await readTask;
        var stderr = await stderrTask;
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            Console.WriteLine("ffmpeg WebP encode failed ({0}): {1}", process.ExitCode, stderr.Trim());
            outputStream.Dispose();
            return null;
        }

        outputStream.Position = 0;
        return outputStream;
    }

    private static async Task WritePngFramesToStdinAsync(Process process, byte[][] frameBytes)
    {
        await using var stdin = process.StandardInput.BaseStream;
        foreach (var frame in frameBytes)
        {
            await stdin.WriteAsync(frame);
        }
    }

    private static async Task<MemoryStream> EncodeWebpViaImg2WebpAsync(byte[][] frameBytes)
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wheelbot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var framePaths = new string[frameBytes.Length];
            for (var i = 0; i < frameBytes.Length; i++)
            {
                framePaths[i] = System.IO.Path.Combine(tempDir, $"frame_{i:D4}.png");
                await File.WriteAllBytesAsync(framePaths[i], frameBytes[i]);
            }

            var outputPath = System.IO.Path.Combine(tempDir, "animation.webp");
            var frameDelayMs = Math.Max(1, (int)Math.Round(1000 / AnimationFramerate));
            var args = new List<string> { "-loop", "0", "-d", frameDelayMs.ToString(), "-o", outputPath };
            args.AddRange(framePaths);

            var psi = new ProcessStartInfo
            {
                FileName = ResolveImg2WebpPath(),
                ArgumentList = { },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start img2webp. Install the webp package (e.g. brew install webp).");

            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new InvalidOperationException($"img2webp failed ({process.ExitCode}): {stderr.Trim()}");
            }

            var bytes = await File.ReadAllBytesAsync(outputPath);
            if (!IsValidAnimatedWebp(bytes))
            {
                throw new InvalidOperationException("img2webp produced output that is not a valid animated WebP.");
            }

            return new MemoryStream(bytes);
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

    private static string ResolveImg2WebpPath()
    {
        if (OperatingSystem.IsMacOS())
        {
            string[] candidates =
            [
                "/opt/homebrew/bin/img2webp",
                "/usr/local/bin/img2webp",
            ];
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return "img2webp";
    }

    private static double Factor(float x, float fullRotation) => ((fullRotation - (1 / fullRotation) * float.Pow(x, 2)) / fullRotation);

    private byte[] RenderPointerPngBytes()
    {
        using var img = MakeSmallTriangle(10);
        using var stream = new MemoryStream();
        img.SaveAsPng(stream);
        return stream.ToArray();
    }

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
