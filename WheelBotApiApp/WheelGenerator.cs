using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Diagnostics;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Drawing;
using IPath = SixLabors.ImageSharp.Drawing.IPath;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace WheelBot;

public sealed class WheelGenerator
{
    private readonly Color[] _colors = { Color.Red, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Purple };
    private readonly List<string> _options = new();
    private readonly Random _random = new Random();
    private FontCollection _collection = new();
    private const int SIZE = 400;

    public bool HasOptions() => _options.Any();
    public List<string> Options => _options;
    public WheelGenerator()
    {
        _collection.AddSystemFonts();
    }

    public async Task<AnimatedWheel> GenerateAnimation()
    {
        var selectedIndex = _random.Next(_options.Count);

        var rotation = CalculateRotation(selectedIndex, _options.Count);

        //await CreateAnimation(600, _colors, _options, "FullAnimation.gif");
        var stream = await CreateAnimation(SIZE, 360*2 + rotation, true);

        return new AnimatedWheel(selectedIndex, _options[selectedIndex], stream);
    }

    public void RandomizeOrder()
    {
        var newOrder = _options.OrderBy(x => _random.Next(5000)).ToList();
        _options.Clear();
        _options.AddRange(newOrder);
    }

    public void AddOption(string option)
    {
        _options.Add(option);
    }

    int CalculateRotation(int selectedIndex, int length)
    {
        var sweepAngle = 360 / length;
        var targetAngle = sweepAngle * (selectedIndex) + sweepAngle / 2;
        return 360 - targetAngle + 5;
    }


    private IPath MakeSlicePath(int r, float angle, float sweep)
    {
        var path = new PathBuilder();
        // Calculate the center point of the circle
        var center = new PointF(r, r);

        // Calculate the start angle of the slice in radians
        var startAngle = angle * Math.PI / 180;

        // Calculate the end angle of the slice in radians
        var endAngle = (angle + sweep) * Math.PI / 180;

        // Calculate the coordinates of the first corner of the slice
        var x1 = r * Math.Cos(startAngle) + center.X;
        var y1 = r * Math.Sin(startAngle) + center.Y;

        // Calculate the coordinates of the second corner of the slice
        var x2 = r * Math.Cos(endAngle) + center.X;
        var y2 = r * Math.Sin(endAngle) + center.Y;

        // Create points for the first and second corners of the slice
        var corner1 = new PointF((float)x1, (float)y1);
        var corner2 = new PointF((float)x2, (float)y2);

        path.AddLine(center, corner1);
        path.AddArc(center, r, r, 0, angle, sweep);
        path.AddLine(center, corner2);

        return path.Build();
    }

    private Image MakeWheelV2(int r)
    {

        // Calculate the start and sweep angles for each slice
        int numSlices = _options.Count;
        float startAngle = 0;
        float sweepAngle = 360 / numSlices;
        var image = new Image<Rgba32>(r * 2, r * 2);

        for (int i = 0; i < numSlices; i++)
        {
            var color = _colors[i % _colors.Length];
            var path = MakeSlicePath(r, i*sweepAngle, sweepAngle);
            image.Mutate(o => o.Fill(color, path));
            image.Mutate(o => o.Draw(new Pen(Color.Black, 1), path));


            image.Mutate(x => x.Rotate(-(i * sweepAngle + sweepAngle / 2)));
            CropToCenter(image, r * 2);
            image.Mutate(x => x.DrawText(_options[i], _collection.Get("Verdana").CreateFont(10), Color.Black, new PointF(r + 20, r-6)));
            image.Mutate(x => x.Rotate((i * sweepAngle + sweepAngle / 2)));
            CropToCenter(image, r * 2);
        }

        return image;
    }

    private static void CropToCenter(Image img, int size)
    {
        var curSize = img.Size();
        img.Mutate(i => i.Crop(new Rectangle((int)Math.Round((curSize.Width - size) / 2d), (int)Math.Round((curSize.Height - size) / 2d), size, size)));
    }

    public async Task<Stream> CreatePreview()
    {
        using Image<Rgba32> png = new(SIZE, SIZE, Color.Transparent);

        png.Mutate(x => x.DrawImage(MakeWheelV2(SIZE / 2), 1));

        MemoryStream stream = new();
        await png.SaveAsPngAsync(stream);
        return stream;
    }

    private async Task<Stream> CreateAnimation(int size, int rotation = 360, bool stop = false)
    {
        using Image<Rgba32> gif = new(SIZE, SIZE, Color.Transparent);
        var gifMetaData = gif.Metadata.GetGifMetadata();
        gifMetaData.RepeatCount = 1;
        GifFrameMetadata metadata = gif.Frames.RootFrame.Metadata.GetGifMetadata();
        metadata.FrameDelay = 2;

        var wheel = MakeWheelV2(size / 2);
        GifFrameMetadata md = wheel.Frames.RootFrame.Metadata.GetGifMetadata();
        md.FrameDelay = 3;

        gif.Mutate(x => x.DrawImage(wheel, 1));

        Stopwatch sw = new();
        sw.Start();

        // Loop to rotate the wheel and save each frame of the animation
        for (int i = 10; i < rotation; i += 10)
        {
            //frames.Add(newframe);
            Console.WriteLine(sw.ElapsedMilliseconds);
            
            var img = new Image<Rgba32>(size, size, Color.Transparent);
            img.Mutate(x => x.DrawImage(wheel, 1));
            wheel.Mutate(x => x.Rotate(10, new BicubicResampler()));
            CropToCenter(wheel, size);
            gif.Frames.AddFrame(wheel.Frames.RootFrame);
        }

        //draw last frame
        var lastFrame = MakeWheelV2(size / 2);
        lastFrame.Mutate(x => x.Rotate(rotation-5));
        CropToCenter(lastFrame, size);
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

    public string RemoveOption(int value)
    {
        var removed = _options[value];
        _options.RemoveAt(value);
        return removed;
    }

    public string RemoveOption(string value)
    {
        _options.Remove(value);
        return value;
    }
}

public class AnimatedWheel
{
    public AnimatedWheel(int selectedIndex, string result, Stream spinningAnimation)
    {
        SelectedIndex = selectedIndex;
        Result = result;
        SpinningAnimation = spinningAnimation;
    }

    public int SelectedIndex { get; set; }
    public string Result { get; set; }
    public Stream SpinningAnimation { get; set; }
}
