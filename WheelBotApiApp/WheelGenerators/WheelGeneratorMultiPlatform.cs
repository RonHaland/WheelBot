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

public sealed class WheelGeneratorMultiPlatform : IWheelGenerator, IWheelBaseRenderer, IWheelAnimationPrecomputer
{
    private const float SharedSpinEndDegrees = 720f;
    private const float SpinStartDegrees = 180f;
    private const float MinAngleStep = 0.5f;
    private const int MaxAnimationFrames = 180;
    private const double AnimationFramerate = 24;
    private const int MaxFfmpegAngleSegments = 32;
    // Cap encoded frames; easing still uses full duration via the rotate expression on t.
    private const int MaxEncodeFrames = 72;
    private const double MinEncodeFps = 16;
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

    public async Task<AnimatedWheel> GenerateAnimation(string wheelId, Wheel wheel, CancellationToken cancellationToken = default)
    {
        var totalSw = Stopwatch.StartNew();
        var options = wheel.Options.ToList();
        var contentHash = WheelContentHash.Compute(options);
        var wheelSnapshot = new Wheel(options);
        var selectedIndex = _random.Next(options.Count);

        cancellationToken.ThrowIfCancellationRequested();

        var cachedSpin = await _baseImageStore.TryDownloadSpinAsync(wheelId, contentHash, selectedIndex, cancellationToken);
        if (cachedSpin is not null)
        {
            totalSw.Stop();
            Console.WriteLine(
                "Spin blob hit for {0} outcome {1} in {2} ms",
                wheelId,
                selectedIndex,
                totalSw.ElapsedMilliseconds);
            return new AnimatedWheel(selectedIndex, options[selectedIndex], new MemoryStream(cachedSpin));
        }

        Console.WriteLine("Spin blob miss for {0} outcome {1}, encoding on demand", wheelId, selectedIndex);
        var encodeSw = Stopwatch.StartNew();
        var webpBytes = await EncodeOutcomeWebpBytesAsync(wheelId, wheelSnapshot, selectedIndex, cancellationToken);
        encodeSw.Stop();
        await _baseImageStore.UploadSpinAsync(wheelId, contentHash, selectedIndex, webpBytes, cancellationToken);
        totalSw.Stop();
        Console.WriteLine(
            "Spin on-demand outcome {0} for {1}: encode {2} ms, total {3} ms ({4} bytes)",
            selectedIndex,
            wheelId,
            encodeSw.ElapsedMilliseconds,
            totalSw.ElapsedMilliseconds,
            webpBytes.Length);

        return new AnimatedWheel(selectedIndex, options[selectedIndex], new MemoryStream(webpBytes));
    }

    public async Task PrecomputeAllAsync(string wheelId, IReadOnlyList<string> options, CancellationToken cancellationToken)
    {
        if (options.Count == 0)
            return;

        var totalSw = Stopwatch.StartNew();
        var contentHash = WheelContentHash.Compute(options);
        var wheel = new Wheel(options.ToList());
        var baseWheelBytes = await EnsureBaseWheelBytesAsync(wheelId, wheel, cancellationToken);

        var sharedSw = Stopwatch.StartNew();
        var sharedAngles = BuildSharedSpinAngles();
        var sharedFrameBytes = await RenderSharedSpinFramesAsync(baseWheelBytes, sharedAngles, cancellationToken);
        sharedSw.Stop();
        Console.WriteLine(
            "Precompute shared frames for {0}: {1} frames in {2} ms",
            wheelId,
            sharedFrameBytes.Count,
            sharedSw.ElapsedMilliseconds);

        var encodedCount = 0;
        for (var index = 0; index < options.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _baseImageStore.TryDownloadSpinAsync(wheelId, contentHash, index, cancellationToken) is not null)
                continue;

            var outcomeSw = Stopwatch.StartNew();
            var webpBytes = await EncodeOutcomeFromSharedFramesAsync(
                sharedFrameBytes,
                baseWheelBytes,
                index,
                options.Count,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await _baseImageStore.UploadSpinAsync(wheelId, contentHash, index, webpBytes, cancellationToken);
            outcomeSw.Stop();
            encodedCount++;
            Console.WriteLine(
                "Precomputed spin-{0} for {1} in {2} ms ({3} bytes)",
                index,
                wheelId,
                outcomeSw.ElapsedMilliseconds,
                webpBytes.Length);
        }

        totalSw.Stop();
        Console.WriteLine(
            "Precompute finished for {0}: {1}/{2} outcomes encoded in {3} ms",
            wheelId,
            encodedCount,
            options.Count,
            totalSw.ElapsedMilliseconds);
    }

    internal static float CalculateRotation(int selectedIndex, int length)
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
        var cached = await _baseImageStore.TryDownloadBaseAsync(wheelId, hash);
        if (cached is not null)
        {
            return new MemoryStream(cached);
        }

        var pngBytes = RenderBaseWheelPng(wheel);
        await _baseImageStore.UploadBaseAsync(wheelId, hash, pngBytes);
        return new MemoryStream(pngBytes);
    }

    private async Task<byte[]> EnsureBaseWheelBytesAsync(string wheelId, Wheel wheel, CancellationToken cancellationToken)
    {
        var hash = WheelContentHash.Compute(wheel.Options);
        var cached = await _baseImageStore.TryDownloadBaseAsync(wheelId, hash, cancellationToken);
        if (cached is null)
        {
            Console.WriteLine("Wheel base blob miss for {0}, rendering synchronously", wheelId);
            cached = RenderBaseWheelPng(wheel);
            await _baseImageStore.UploadBaseAsync(wheelId, hash, cached, cancellationToken);
        }
        else
        {
            Console.WriteLine("Wheel base blob hit for {0}", wheelId);
        }

        return cached;
    }

    private async Task<byte[]> EncodeOutcomeWebpBytesAsync(string wheelId, Wheel wheel, int selectedIndex, CancellationToken cancellationToken)
    {
        var baseSw = Stopwatch.StartNew();
        var baseWheelBytes = await EnsureBaseWheelBytesAsync(wheelId, wheel, cancellationToken);
        baseSw.Stop();

        var sharedSw = Stopwatch.StartNew();
        var sharedAngles = BuildSharedSpinAngles();
        var sharedFrameBytes = await RenderSharedSpinFramesAsync(baseWheelBytes, sharedAngles, cancellationToken);
        sharedSw.Stop();

        var landingSw = Stopwatch.StartNew();
        var webpBytes = await EncodeOutcomeFromSharedFramesAsync(
            sharedFrameBytes,
            baseWheelBytes,
            selectedIndex,
            wheel.Options.Count,
            cancellationToken);
        landingSw.Stop();

        Console.WriteLine(
            "Outcome {0} encode breakdown for {1}: base {2} ms, shared {3} ms ({4} frames), landing+webp {5} ms",
            selectedIndex,
            wheelId,
            baseSw.ElapsedMilliseconds,
            sharedSw.ElapsedMilliseconds,
            sharedFrameBytes.Count,
            landingSw.ElapsedMilliseconds);

        return webpBytes;
    }

    private async Task<IReadOnlyList<byte[]>> RenderSharedSpinFramesAsync(
        byte[] baseWheelBytes,
        IReadOnlyList<float> sharedAngles,
        CancellationToken cancellationToken)
    {
        var frames = new List<byte[]>(sharedAngles.Count);
        foreach (var angle in sharedAngles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            frames.Add(RenderCompositeFramePng(baseWheelBytes, angle));
        }

        return await Task.FromResult(frames);
    }

    private async Task<byte[]> EncodeOutcomeFromSharedFramesAsync(
        IReadOnlyList<byte[]> sharedFrames,
        byte[] baseWheelBytes,
        int selectedIndex,
        int optionCount,
        CancellationToken cancellationToken)
    {
        var landingAngles = BuildLandingAngles(selectedIndex, optionCount);
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wheelbot-spin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            for (var i = 0; i < sharedFrames.Count; i++)
            {
                await File.WriteAllBytesAsync(System.IO.Path.Combine(tempDir, $"{i:D4}.png"), sharedFrames[i], cancellationToken);
            }

            var frameIndex = sharedFrames.Count;
            foreach (var angle in landingAngles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sharedFrames.Count > 0 && frameIndex == sharedFrames.Count && Math.Abs(angle - SharedSpinEndDegrees) < 0.01f)
                    continue;

                var png = RenderCompositeFramePng(baseWheelBytes, angle);
                await File.WriteAllBytesAsync(System.IO.Path.Combine(tempDir, $"{frameIndex:D4}.png"), png, cancellationToken);
                frameIndex++;
            }

            if (frameIndex == 0)
                throw new InvalidOperationException("Spin animation requires at least one frame.");

            var ffmpegSw = Stopwatch.StartNew();
            var webpBytes = await EncodeSpinWebpFromFrameSequenceAsync(tempDir, frameIndex, cancellationToken);
            ffmpegSw.Stop();
            Console.WriteLine(
                "ffmpeg WebP for outcome {0}: {1} frames in {2} ms ({3} bytes)",
                selectedIndex,
                frameIndex,
                ffmpegSw.ElapsedMilliseconds,
                webpBytes.Length);
            return webpBytes;
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

    private byte[] RenderCompositeFramePng(byte[] baseWheelPng, float angleDegrees)
    {
        using var canvas = new Image<Rgba32>(_size, _size, Color.Transparent);
        using var wheelLayer = Image.Load<Rgba32>(baseWheelPng);
        wheelLayer.Mutate(x => x.Rotate(angleDegrees));
        CenterCropToSquare(wheelLayer, _size);

        canvas.Mutate(x =>
        {
            x.DrawImage(wheelLayer, new Point(0, 0), 1f);
            using var pointer = Image.Load<Rgba32>(_pointerPngBytes.Value);
            x.DrawImage(pointer, new Point(0, 0), 1f);
        });

        using var stream = new MemoryStream();
        canvas.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static void CenterCropToSquare(Image<Rgba32> image, int size)
    {
        if (image.Width == size && image.Height == size)
            return;

        var left = (image.Width - size) / 2;
        var top = (image.Height - size) / 2;
        image.Mutate(x => x.Crop(new Rectangle(left, top, size, size)));
    }

    private static List<float> BuildSharedSpinAngles() =>
        BuildAngleList(SpinStartDegrees, SharedSpinEndDegrees, easeOut: false);

    private static List<float> BuildLandingAngles(int selectedIndex, int optionCount)
    {
        var finalRotation = SharedSpinEndDegrees + CalculateRotation(selectedIndex, optionCount);
        return BuildAngleList(SharedSpinEndDegrees, finalRotation, easeOut: true);
    }

    private static List<float> BuildFullAnglesForOutcome(int selectedIndex, int optionCount)
    {
        var shared = BuildSharedSpinAngles();
        var landing = BuildLandingAngles(selectedIndex, optionCount);
        if (landing.Count > 0 && shared.Count > 0 && Math.Abs(landing[0] - shared[^1]) < 0.01f)
            landing.RemoveAt(0);

        shared.AddRange(landing);
        return shared;
    }

    private static List<float> BuildAngleList(float startDegrees, float endRotation, bool easeOut)
    {
        var angles = new List<float>();
        for (var i = startDegrees; i < endRotation && angles.Count < MaxAnimationFrames - 1;)
        {
            angles.Add(i);
            var step = easeOut
                ? Math.Max(MinAngleStep, (float)(20 * Factor(i, endRotation)))
                : 20f;
            i += step;
        }

        if (angles.Count == 0 || Math.Abs(angles[^1] - endRotation) > 0.01f)
        {
            angles.Add(endRotation);
        }

        if (angles.Count > MaxAnimationFrames)
        {
            var sampled = new List<float>(MaxAnimationFrames);
            for (var s = 0; s < MaxAnimationFrames - 1; s++)
            {
                var index = (int)Math.Round(s * (angles.Count - 1) / (double)(MaxAnimationFrames - 1));
                sampled.Add(angles[index]);
            }

            sampled.Add(endRotation);
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

    private async Task<byte[]> EncodeSpinWebpFromFrameSequenceAsync(string framesDirectory, int frameCount, CancellationToken cancellationToken)
    {
        // Keep the logical spin duration, but never feed ffmpeg a prefix of the sequence —
        // landings are at the end, so a MaxEncodeFrames head-truncation always misses the target.
        var durationSeconds = frameCount / AnimationFramerate;
        var encodeIndices = SelectEncodeFrameIndices(frameCount, MaxEncodeFrames);
        var encodeFrameCount = encodeIndices.Count;
        // Exact fps so subsampled frames span the full logical duration (do not bump fps —
        // that would shorten the spin and was part of the old prefix-truncation path).
        var encodeFps = encodeFrameCount / durationSeconds;

        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wheelbot-{Guid.NewGuid():N}");
        var sampleDir = System.IO.Path.Combine(tempDir, "frames");
        Directory.CreateDirectory(sampleDir);
        var outputPath = System.IO.Path.Combine(tempDir, "spin.webp");

        try
        {
            for (var i = 0; i < encodeIndices.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = System.IO.Path.Combine(framesDirectory, $"{encodeIndices[i]:D4}.png");
                var dest = System.IO.Path.Combine(sampleDir, $"{i:D4}.png");
                File.Copy(source, dest);
            }

            var inputPattern = System.IO.Path.Combine(sampleDir, "%04d.png");
            var duration = durationSeconds.ToString(CultureInfo.InvariantCulture);
            var framerate = encodeFps.ToString(CultureInfo.InvariantCulture);
            var frameCountStr = encodeFrameCount.ToString(CultureInfo.InvariantCulture);

            var stderr = await RunFfmpegFrameSequenceEncodeAsync(
                inputPattern,
                outputPath,
                framerate,
                duration,
                frameCountStr,
                encoder: "libwebp_anim",
                cancellationToken);

            if (!File.Exists(outputPath) || !IsValidAnimatedWebp(await File.ReadAllBytesAsync(outputPath, cancellationToken)))
            {
                stderr = await RunFfmpegFrameSequenceEncodeAsync(
                    inputPattern,
                    outputPath,
                    framerate,
                    duration,
                    frameCountStr,
                    encoder: "libwebp",
                    cancellationToken);
            }

            if (!File.Exists(outputPath))
                throw new InvalidOperationException($"ffmpeg WebP encode from frames failed: {stderr.Trim()}");

            var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            if (!IsValidAnimatedWebp(bytes))
                throw new InvalidOperationException("ffmpeg produced output that is not a valid animated WebP.");

            using var stream = new MemoryStream(bytes);
            PatchAnimatedWebpPlayback(stream, loopCount: 1, lastFrameDurationMs: FinalFrameHoldMs);
            return stream.ToArray();
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

    /// <summary>
    /// Evenly picks up to <paramref name="maxFrames"/> indices from [0, frameCount), always including the first and last frame.
    /// </summary>
    internal static IReadOnlyList<int> SelectEncodeFrameIndices(int frameCount, int maxFrames)
    {
        if (frameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        if (maxFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFrames));

        if (frameCount <= maxFrames)
            return Enumerable.Range(0, frameCount).ToArray();

        var indices = new List<int>(maxFrames);
        for (var s = 0; s < maxFrames; s++)
        {
            var index = (int)Math.Round(s * (frameCount - 1) / (double)(maxFrames - 1));
            if (indices.Count == 0 || indices[^1] != index)
                indices.Add(index);
        }

        if (indices[^1] != frameCount - 1)
            indices.Add(frameCount - 1);

        return indices;
    }

    private async Task<MemoryStream> EncodeSpinWebpViaFfmpegAsync(
        byte[] baseWheelPng,
        byte[] pointerPng,
        IReadOnlyList<float> angles,
        CancellationToken cancellationToken)
    {
        var durationSeconds = angles.Count / AnimationFramerate;
        var encodeFrameCount = Math.Min(angles.Count, MaxEncodeFrames);
        var encodeFps = Math.Clamp(encodeFrameCount / durationSeconds, MinEncodeFps, AnimationFramerate);
        encodeFrameCount = Math.Max(1, (int)Math.Ceiling(durationSeconds * encodeFps));
        Console.WriteLine(
            "WebP encode plan: {0} logical frames, {1} output frames at {2:F1} fps over {3:F2}s",
            angles.Count,
            encodeFrameCount,
            encodeFps,
            durationSeconds);

        var keyframes = SubsampleAnglesForFfmpeg(angles);
        var rotateExpression = BuildFfmpegRotateAngleExpression(keyframes, durationSeconds);
        var duration = durationSeconds.ToString(CultureInfo.InvariantCulture);
        var framerate = encodeFps.ToString(CultureInfo.InvariantCulture);
        var frameCount = encodeFrameCount.ToString(CultureInfo.InvariantCulture);

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
                encoder: "libwebp_anim",
                cancellationToken);

            if (!File.Exists(outputPath) || !IsValidAnimatedWebp(await File.ReadAllBytesAsync(outputPath, cancellationToken)))
            {
                Console.WriteLine("libwebp_anim failed or produced invalid output; retrying with libwebp: {0}", stderr.Trim());
                stderr = await RunFfmpegSpinEncodeAsync(
                    basePath,
                    pointerPath,
                    outputPath,
                    filterComplex.ToString(),
                    duration,
                    frameCount,
                    encoder: "libwebp",
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException($"ffmpeg WebP encode failed: {stderr.Trim()}");
            }

            var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);
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

    private Task<string> RunFfmpegSpinEncodeAsync(
        string basePath,
        string pointerPath,
        string outputPath,
        string filterComplex,
        string duration,
        string frameCount,
        string encoder,
        CancellationToken cancellationToken) =>
        RunFfmpegAsync(BuildFfmpegSpinArguments(basePath, pointerPath, outputPath, filterComplex, duration, frameCount, encoder), cancellationToken);

    private async Task<string> RunFfmpegFrameSequenceEncodeAsync(
        string inputPattern,
        string outputPath,
        string framerate,
        string duration,
        string frameCount,
        string encoder,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-framerate", framerate,
            "-i", inputPattern,
            "-t", duration,
            "-frames:v", frameCount,
            "-an",
            "-c:v", encoder,
            "-loop", "1",
            "-preset", "picture",
            "-compression_level", "0",
            "-q:v", "78",
            "-threads", "0",
            "-pix_fmt", "yuva420p",
            "-f", "webp",
            outputPath,
        };

        return await RunFfmpegAsync(args, cancellationToken);
    }

    private static IEnumerable<string> BuildFfmpegSpinArguments(
        string basePath,
        string pointerPath,
        string outputPath,
        string filterComplex,
        string duration,
        string frameCount,
        string encoder)
    {
        yield return "-hide_banner";
        yield return "-loglevel";
        yield return "error";
        yield return "-y";
        yield return "-loop";
        yield return "1";
        yield return "-t";
        yield return duration;
        yield return "-i";
        yield return basePath;
        yield return "-loop";
        yield return "1";
        yield return "-t";
        yield return duration;
        yield return "-i";
        yield return pointerPath;
        yield return "-filter_complex";
        yield return filterComplex;
        yield return "-map";
        yield return "[out]";
        yield return "-frames:v";
        yield return frameCount;
        yield return "-an";
        yield return "-c:v";
        yield return encoder;
        yield return "-loop";
        yield return "1";
        yield return "-preset";
        yield return "picture";
        yield return "-compression_level";
        yield return "0";
        yield return "-q:v";
        yield return "78";
        yield return "-threads";
        yield return "0";
        yield return "-pix_fmt";
        yield return "yuva420p";
        yield return "-f";
        yield return "webp";
        yield return outputPath;
    }

    private async Task<string> RunFfmpegAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        await using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // best-effort kill
            }
        });

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            Console.WriteLine("ffmpeg exited with {0}: {1}", process.ExitCode, stderr.Trim());

        cancellationToken.ThrowIfCancellationRequested();
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
