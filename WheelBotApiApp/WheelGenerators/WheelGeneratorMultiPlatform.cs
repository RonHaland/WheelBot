using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Numerics;

namespace WheelBotApiApp.WheelGenerators;

public sealed class WheelGeneratorMultiPlatform : IWheelGenerator
{
    private readonly Color[] _colors = [Color.Red, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Purple];
    private readonly Random _random = new Random();
    private readonly FontCollection _collection = new();
    private readonly int _size = 420;
    
    private DrawingOptions DrawingOptions => new()
    {
        GraphicsOptions = new GraphicsOptions
        {
            Antialias = true,
        }
    };

    public WheelGeneratorMultiPlatform(IConfiguration configuration)
    {
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
        }

        if (!_collection.Families.Any())
        {
            _collection.AddSystemFonts();
        }
    }

    public async Task<AnimatedWheel> GenerateAnimation(Wheel wheel)
    {
        var selectedIndex = _random.Next(wheel.Options.Count);

        var rotation = CalculateRotation(selectedIndex, wheel.Options.Count);

        var stream = await CreateAnimation(wheel, 360 * 2 + rotation, true);

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
        
        var endAngle = (angle + sweep) * Math.PI / 180d;
        
        var x1 = r * Math.Cos(startAngle) + center.X;
        var y1 = r * Math.Sin(startAngle) + center.Y;
        
        var x2 = r * Math.Cos(endAngle) + center.X;
        var y2 = r * Math.Sin(endAngle) + center.Y;
        
        var corner1 = new PointF((float)x1, (float)y1);
        var corner2 = new PointF((float)x2, (float)y2);
        
        path.MoveTo(center);
        path.LineTo(corner1);
        path.ArcTo(r,r, 0, false, true, corner2);
        path.CloseFigure();

        return path.Build();
    }

    private Image<Rgba32> MakeWheel(int r, Wheel wheel, float rotationDegrees = 0)
    {
        var numSlices = wheel.Options.Count;
        var sweepAngle = 360f / numSlices;
        var image = new Image<Rgba32>(r * 2, r * 2);
        var center = new PointF(r, r);
        var font = _collection.Get("Roboto").CreateFont(15, FontStyle.Regular);
        var emojiFont = _collection.Get("Noto Color Emoji");

        for (var i = 0; i < numSlices; i++)
        {
            var wheelEntryText = wheel.Options[i];
            var color = i == numSlices - 1 && numSlices == _colors.Length + 1 ? _colors[_colors.Length/2] : _colors[i % _colors.Length];
            var startAngle = (rotationDegrees + sweepAngle * i) % 360 ;
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
                FallbackFontFamilies = [emojiFont],
            };
            var drawingOpts = new DrawingOptions
            {
                Transform = Matrix3x2.CreateRotation(textAngleRadians, textPosition)
            };
            image.Mutate(o =>
            {
                o.Clip(path, ctx => 
                    ctx.DrawText(drawingOpts, textOpts, wheelEntryText, null, new SolidPen(Color.Black)));
            });
        }

        return image;
    }

    private static void CropToCenter(Image img, int size)
    {
        var curSize = img.Size;
        img.Mutate(i => i.Crop(new Rectangle((int)Math.Floor((curSize.Width - size) / 2d), (int)Math.Floor((curSize.Height - size) / 2d), size, size)));
    }

    public async Task<Stream> CreatePreview(Wheel wheel)
    {
        using Image<Rgba32> png = new(_size, _size, Color.Transparent);

        png.Mutate(x => x.DrawImage(MakeWheel(_size / 2, wheel), 1));

        MemoryStream stream = new();
        await png.SaveAsPngAsync(stream);
        return stream;
    }

    private async Task<Stream> CreateAnimation(Wheel wheel, float rotation = 360f, bool stop = false)
    {
        var sw = Stopwatch.StartNew();
        using Image<Rgba32> gif = new(_size, _size, Color.Transparent);
        var gifMetaData = gif.Metadata.GetGifMetadata();
        gifMetaData.RepeatCount = 1;
        GifFrameMetadata metadata = gif.Frames.RootFrame.Metadata.GetGifMetadata();
        metadata.FrameDelay = 2;
        
        var targetTriangle = MakeSmallTriangle(10);
        var wheelImage = MakeWheel(_size / 2, wheel);
        Console.WriteLine("Generated wheel at {0}", sw.ElapsedMilliseconds);
        GifFrameMetadata md = wheelImage.Frames.RootFrame.Metadata.GetGifMetadata();
        md.FrameDelay = 3;

        gif.Mutate(x =>
        {
            x.DrawImage(wheelImage, DrawingOptions.GraphicsOptions);
            x.DrawImage(targetTriangle, DrawingOptions.GraphicsOptions);
        });

        List<Task<Image<Rgba32>>> rotatedWheelsTasks = [];

        for (var i = 180f; i < rotation; i += float.Max(0.66f, (float)(20 * Factor(i, rotation))))
        {
            var degrees = i;
            var task = Task.Run(() => MakeWheel(_size / 2, wheel, degrees));
            rotatedWheelsTasks.Add(task);
        }
        
        await Task.WhenAll(rotatedWheelsTasks);
        Console.WriteLine("All frames ({1}) generated at {0}", sw.ElapsedMilliseconds, rotatedWheelsTasks.Count);
        
        var wheelFrames = rotatedWheelsTasks.Select(t => t.Result);

        foreach (var frame in wheelFrames)
        {
            GifFrameMetadata md2 = frame.Frames.RootFrame.Metadata.GetGifMetadata();
            md2.FrameDelay = 3;
            
            frame.Mutate(x => x.DrawImage(targetTriangle,  DrawingOptions.GraphicsOptions));

            gif.Frames.AddFrame(frame.Frames.RootFrame);
            frame.Dispose();
        }

        //draw last frame
        var lastFrame = MakeWheel(_size / 2, wheel, rotation);
        lastFrame.Mutate(o => o.DrawImage(targetTriangle, DrawingOptions.GraphicsOptions));
        GifFrameMetadata lastFMd = lastFrame.Frames.RootFrame.Metadata.GetGifMetadata();
        lastFMd.FrameDelay = 2;
        gif.Frames.AddFrame(lastFrame.Frames.RootFrame);


        Console.WriteLine("Drawn all frames at {0}", sw.ElapsedMilliseconds);
        Stream stream = new MemoryStream();

        // Save the animation to a stream
        await gif.SaveAsGifAsync(stream);
        Console.WriteLine("Saved all frames at {0}, size: {1}", sw.ElapsedMilliseconds, stream.Length);
        sw.Stop();
        return stream;
    }
    
    private static double Factor(float x, float fullRotation) => ((fullRotation - (1/fullRotation) * float.Pow(x, 2))/ fullRotation);
    
    private Image<Rgba32> MakeSmallTriangle(int height)
    {
        var img = new Image<Rgba32>(_size, _size);

        var pen = new SolidPen(Color.Black, 2);
        var brush = new SolidBrush(Color.DarkGray);
        PointF[] points =
        [
            new (_size - 1, _size / 2f + height / 2f),
            new (_size - 1, _size / 2f - height / 2f),
            new (_size - height, _size / 2f)
        ];
        img.Mutate(i => i.FillPolygon(brush, points));
        img.Mutate(i => i.DrawPolygon(pen, points));

        return img;
    }
}
