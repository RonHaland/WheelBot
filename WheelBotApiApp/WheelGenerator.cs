using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;
using IPath = SixLabors.ImageSharp.Drawing.IPath;
using Pen = SixLabors.ImageSharp.Drawing.Processing.Pen;
using PointF = SixLabors.ImageSharp.PointF;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace WheelBot;

public sealed class WheelGenerator
{
    private readonly Color[] _colors = { Color.Red, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Purple };
    private readonly System.Drawing.Color[] _colors2 = { System.Drawing.Color.Red, System.Drawing.Color.Orange, System.Drawing.Color.Yellow, System.Drawing.Color.Green, System.Drawing.Color.Blue, System.Drawing.Color.Purple };
    private readonly List<string> _options = new();
    private readonly Random _random = new Random();
    private FontCollection _collection = new();
    private const int SIZE = 420;

    public bool HasOptions() => _options.Any();
    public List<string> Options => _options;
    public WheelGenerator(List<string> options)
    {
        _collection.AddSystemFonts();
        _options = options;
    }
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

    private Image MakeWheel(int r)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return MakeWheelV1(r);
        }
        return MakeWheelV2(r);
    }

    private Image MakeWheelV1(int r)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception();

        // Calculate the start and sweep angles for each slice
        int numSlices = _options.Count;
        float startAngle = 0;
        float sweepAngle = (360 / (float)numSlices);
        var diameter = r * 2;
        var img = new System.Drawing.Bitmap(diameter, diameter);
        var g = Graphics.FromImage(img);

        // Draw the slices of the wheel
        for (int i = 0; i < numSlices; i++)
        {
            // Fill the slice with the appropriate color
            Brush brush = new System.Drawing.SolidBrush(_colors2[i % _colors2.Length]);
            g.FillPie(brush, 0, 0, diameter, diameter, startAngle, sweepAngle);

            // Draw the outline of the slice
            System.Drawing.Pen pen = new(System.Drawing.Color.Black, 2);
            g.DrawArc(pen, 0, 0, diameter, diameter, startAngle, sweepAngle);

            // Calculate the size and position of the text
            var fontReduction = _options[i].Length < 13 ? 35 : (int)(_options[i].Length * 2.2);
            System.Drawing.Font font = new("Verdana", diameter / fontReduction);
            System.Drawing.SizeF textSize = g.MeasureString(_options[i], font);
            float x = diameter / 2 + diameter / 10;
            float y = diameter / 2 - textSize.Height / 2;

            var currentRotation = g.Transform.Clone();

            // Create a rotated Matrix object
            Matrix matrix = g.Transform;
            matrix.RotateAt(startAngle + sweepAngle / 2, new System.Drawing.PointF(diameter / 2, diameter / 2));


            // Set the current transformation matrix to the rotated Matrix
            g.Transform = matrix;

            // Draw the text on the slice
            var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
            g.DrawString(_options[i], font, textBrush, x, y);

            // Reset the current transformation matrix
            g.Transform = currentRotation;

            // Update the start angle for the next slice
            startAngle += sweepAngle;
        }
        Image result = Image.Load(ImageToBytes(img));
        return result;
    }

    private Image MakeWheelV2(int r)
    {
        // Calculate the start and sweep angles for each slice
        int numSlices = _options.Count;
        float sweepAngle = (360 / (float)numSlices);
        var image = new Image<Rgba32>(r * 2, r * 2);

        for (int i = 0; i < numSlices; i++)
        {
            var color = _colors[i % _colors.Length];
            var path = MakeSlicePath(r, i*sweepAngle, sweepAngle);
            image.Mutate(o => o.Fill(color, path));
            image.Mutate(o => o.Draw(new Pen(Color.Black, 2), path));

            var sampler = new BicubicResampler();

            image.Mutate(x => x.Rotate(-(i * sweepAngle + sweepAngle / 2), sampler));
            CropToCenter(image, r * 2);
            image.Mutate(x => x.DrawText(_options[i], _collection.Get("Verdana").CreateFont(15), Color.Black, new PointF(r + 20, r-7)));
            image.Mutate(x => x.Rotate((i * sweepAngle + sweepAngle / 2), sampler));
            CropToCenter(image, r * 2);
        }

        return image;
    }

    private static void CropToCenter(Image img, int size)
    {
        var curSize = img.Size();
        img.Mutate(i => i.Crop(new Rectangle((int)Math.Floor((curSize.Width - size) / 2d), (int)Math.Floor((curSize.Height - size) / 2d), size, size)));
    }

    public async Task<Stream> CreatePreview()
    {
        using Image<Rgba32> png = new(SIZE, SIZE, Color.Transparent);

        png.Mutate(x => x.DrawImage(MakeWheel(SIZE / 2), 1));

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

        var wheel = MakeWheel(size / 2);
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
            
            Image<Rgba32> img = new(SIZE, SIZE, Color.Transparent);
            img.Mutate(x => x.DrawImage(wheel, 1).Rotate(i));
            CropToCenter(img, size);
            GifFrameMetadata md2 = img.Frames.RootFrame.Metadata.GetGifMetadata();
            md2.FrameDelay = 3;
            //await img.SaveAsPngAsync($"image{i}.png");

            gif.Frames.AddFrame(img.Frames.RootFrame);
            img.Dispose();
        }

        //draw last frame
        var lastFrame = MakeWheel(size / 2);
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

    static byte[]? ImageToBytes(System.Drawing.Image img)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception();

        ImageConverter converter = new();
        return (byte[]?)converter.ConvertTo(img, typeof(byte[]));
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
