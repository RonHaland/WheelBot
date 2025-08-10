using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using System.Diagnostics;

namespace WheelBotApiApp.WheelGenerators;

public sealed class WheelGeneratorMultiPlatform : IWheelGenerator
{
    private readonly Color[] _colors = [Color.Red, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Purple];
    private readonly Random _random = new Random();
    private readonly FontCollection _collection = new();
    private readonly int _size = 420;

    public WheelGeneratorMultiPlatform()
    {
        _collection.AddSystemFonts();
    }

    public async Task<AnimatedWheel> GenerateAnimation(Wheel wheel)
    {
        var selectedIndex = _random.Next(wheel.Options.Count);

        var rotation = CalculateRotation(selectedIndex, wheel.Options.Count);

        var stream = await CreateAnimation(wheel, 360 * 2 + rotation, true);

        return new AnimatedWheel(selectedIndex, wheel.Options[selectedIndex], stream);
    }

    static int CalculateRotation(int selectedIndex, int length)
    {
        var sweepAngle = 360 / length;
        var targetAngle = sweepAngle * selectedIndex + sweepAngle / 2;
        return 360 - targetAngle + 5;
    }

    private static IPath MakeSlicePath(int r, float angle, float sweep)
    {
        var path = new PathBuilder();
        var center = new PointF(r, r);
        
        var startAngle = angle * Math.PI / 180;

        var endAngle = (angle + sweep) * Math.PI / 180;

        var x1 = r * Math.Cos(startAngle) + center.X;
        var y1 = r * Math.Sin(startAngle) + center.Y;

        var x2 = r * Math.Cos(endAngle) + center.X;
        var y2 = r * Math.Sin(endAngle) + center.Y;

        var corner1 = new PointF((float)x1, (float)y1);
        var corner2 = new PointF((float)x2, (float)y2);

        path.AddLine(center, corner1);
        path.AddArc(center, r, r, 0, angle, sweep);
        path.AddLine(center, corner2);

        return path.Build();
    }

    private Image<Rgba32> MakeWheel(int r, Wheel wheel)
    {
        var numSlices = wheel.Options.Count;
        var sweepAngle = 360 / (float)numSlices;
        var image = new Image<Rgba32>(r * 2, r * 2);

        for (var i = 0; i < numSlices; i++)
        {
            var color = _colors[i % _colors.Length];
            var path = MakeSlicePath(r, i * sweepAngle, sweepAngle);
            image.Mutate(o => o.Fill(color, path));
            image.Mutate(o => o.Draw(new SolidPen(Color.Black, 2), path));

            var sampler = new BicubicResampler();

            image.Mutate(x => x.Rotate(-(i * sweepAngle + sweepAngle / 2), sampler));
            CropToCenter(image, r * 2);
            image.Mutate(x => x.DrawText(wheel.Options[i], _collection.Get("Verdana").CreateFont(15), Color.Black, new PointF(r + 20, r - 7)));
            image.Mutate(x => x.Rotate(i * sweepAngle + sweepAngle / 2, sampler));
            CropToCenter(image, r * 2);
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

    private async Task<Stream> CreateAnimation(Wheel wheel, int rotation = 360, bool stop = false)
    {
        using Image<Rgba32> gif = new(_size, _size, Color.Transparent);
        var gifMetaData = gif.Metadata.GetGifMetadata();
        gifMetaData.RepeatCount = 1;
        GifFrameMetadata metadata = gif.Frames.RootFrame.Metadata.GetGifMetadata();
        metadata.FrameDelay = 2;

        var targetTriangle = MakeSmallTriangle(10);
        var wheelImage = MakeWheel(_size / 2, wheel);
        GifFrameMetadata md = wheelImage.Frames.RootFrame.Metadata.GetGifMetadata();
        md.FrameDelay = 3;

        gif.Mutate(x => x.DrawImage(wheelImage, 1));

        Stopwatch sw = new();
        sw.Start();

        for (var i = 10; i < rotation; i += 10)
        {
            Image<Rgba32> img = new(_size, _size, Color.Transparent);
            img.Mutate(x => x.DrawImage(wheelImage, 1).Rotate(i));
            CropToCenter(img, _size);

            img.Mutate(o => o.DrawImage(targetTriangle, 1));
            GifFrameMetadata md2 = img.Frames.RootFrame.Metadata.GetGifMetadata();
            md2.FrameDelay = 3;

            gif.Frames.AddFrame(img.Frames.RootFrame);
            img.Dispose();
        }

        //draw last frame
        var lastFrame = MakeWheel(_size / 2, wheel);
        lastFrame.Mutate(x => x.Rotate(rotation - 5));
        CropToCenter(lastFrame, _size);
        lastFrame.Mutate(o => o.DrawImage(targetTriangle, 1));
        GifFrameMetadata lastFMd = lastFrame.Frames.RootFrame.Metadata.GetGifMetadata();
        lastFMd.FrameDelay = 2;
        gif.Frames.AddFrame(lastFrame.Frames.RootFrame);


        Console.WriteLine("Drawn all frames at {0}", sw.ElapsedMilliseconds);
        Stream stream = new MemoryStream();

        // Save the animation to a stream
        await gif.SaveAsGifAsync(stream);
        Console.WriteLine("Saved all frames at {0}", sw.ElapsedMilliseconds);
        sw.Stop();
        return stream;
    }
    
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
