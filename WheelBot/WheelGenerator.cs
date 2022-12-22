using ImageMagick;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WheelBot;

public sealed class WheelGenerator
{
    private readonly Color[] _colors = { Color.Red, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Purple };
    private readonly List<string> _options = new();
    private readonly Random _random = new Random();
    private const int SIZE = 320;

    public bool HasOptions() => _options.Any();
    public List<string> Options => _options;

    public async Task<AnimatedWheel> GenerateAnimation()
    {
        var selectedIndex = _random.Next(_options.Count);

        var rotation = CalculateRotation(selectedIndex, _options.Count);

        //await CreateAnimation(600, _colors, _options, "FullAnimation.gif");
        var stream = await CreateAnimation(SIZE, _colors, _options, 360*2 + rotation, true);

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

    static void MakeWheel(int size, Graphics g, Color[] colors, IList<string> options)
    {
        // Calculate the start and sweep angles for each slice
        int numSlices = options.Count;
        float startAngle = 0;
        float sweepAngle = 360 / numSlices;

        // Draw the slices of the wheel
        for (int i = 0; i < numSlices; i++)
        {
            // Fill the slice with the appropriate color
            Brush brush = new SolidBrush(colors[i % colors.Length]);
            g.FillPie(brush, 0, 0, size, size, startAngle, sweepAngle);

            // Draw the outline of the slice
            Pen pen = new Pen(Color.Black, 2);
            g.DrawArc(pen, 0, 0, size, size, startAngle, sweepAngle);

            // Calculate the size and position of the text
            var fontReduction = options[i].Length < 25 ? 25 : (int)(options[i].Length*1.5);
            Font font = new Font("Verdana", size / fontReduction);
            SizeF textSize = g.MeasureString(options[i], font);
            float x = size / 2 + size / 10;
            float y = size / 2 - textSize.Height / 2;

            var currentRotation = g.Transform.Clone();

            // Create a rotated Matrix object
            Matrix matrix = g.Transform;
            matrix.RotateAt(startAngle + sweepAngle / 2, new PointF(size / 2, size / 2));


            // Set the current transformation matrix to the rotated Matrix
            g.Transform = matrix;

            // Draw the text on the slice
            Brush textBrush = new SolidBrush(Color.Black);
            g.DrawString(options[i], font, textBrush, x, y);

            // Reset the current transformation matrix
            g.Transform = currentRotation;

            // Update the start angle for the next slice
            startAngle += sweepAngle;
        }
    }

    static IEnumerable<byte[]?> ImagesToBytes(IEnumerable<Image> imgs)
    {
        ImageConverter converter = new ImageConverter();
        return imgs.Select(i => (byte[]?)converter.ConvertTo(i, typeof(byte[])));
    }

    public Stream CreatePreview()
    {
        Bitmap bmp = new Bitmap(SIZE, SIZE);
        Graphics g = Graphics.FromImage(bmp);
        MakeWheel(SIZE, g, _colors, _options);
        MemoryStream stream = new();
        bmp.Save(stream, ImageFormat.Png);
        return stream;
    }

    static async Task<Stream> CreateAnimation(int size, Color[] colors, IList<string> options, int rotation = 360, bool stop = false)
    {
        Bitmap bmp = new Bitmap(size, size);

        // Create a new Graphics object to draw on the Bitmap
        Graphics g = Graphics.FromImage(bmp);
        List<Bitmap> frames = new();

        Stopwatch sw = new();
        sw.Start();

        // Loop to rotate the wheel and save each frame of the animation
        for (int i = 0; i < rotation; i += 10)
        {
            // Clear the graphics object
            g.Clear(Color.Transparent);

            // Create a rotated Matrix object
            Matrix matrix = new Matrix();
            matrix.RotateAt(i, new PointF(size / 2, size / 2));

            // Set the current transformation matrix to the rotated Matrix
            g.Transform = matrix;

            // Draw the slices of the wheel
            MakeWheel(size, g, colors, options);
            // Reset the current transformation matrix
            g.ResetTransform();

            // Save the image to a file
            Bitmap newframe = (Bitmap)bmp.Clone();
            //newframe.Save($"wheel{i}.png", ImageFormat.Png);
            frames.Add(newframe);
            Console.WriteLine(sw.ElapsedMilliseconds);
        }

        if(true)
        {
            Console.WriteLine(rotation);
            g.Clear(Color.Transparent);

            // Create a rotated Matrix object
            Matrix matrix = new Matrix();
            matrix.RotateAt(rotation-5, new PointF(size / 2, size / 2));

            // Set the current transformation matrix to the rotated Matrix
            g.Transform = matrix;

            // Draw the slices of the wheel
            MakeWheel(size, g, colors, options);
            // Reset the current transformation matrix
            g.ResetTransform();

            // Save the image to a file
            Bitmap newframe = (Bitmap)bmp.Clone();
            //newframe.Save($"wheel{i}.png", ImageFormat.Png);
            frames.Add(newframe);
            Console.WriteLine(sw.ElapsedMilliseconds);
        }

        Console.WriteLine("After generating frames {0}", sw.ElapsedMilliseconds);
        // Create an empty list of MagickImage objects
        var images = new List<MagickImage>();

        // Convert the bitmaps to MagickImage objects and add them to the list
        foreach (var frame in ImagesToBytes(frames))
        {
            var image = new MagickImage(frame ?? Array.Empty<byte>());
            image.Alpha(AlphaOption.Discrete);
            images.Add(image);
        }

        Console.WriteLine("Converting frames {0}", sw.ElapsedMilliseconds);

        // Create an animation from the list of images
        var animation = new MagickImageCollection(images);

        animation.Coalesce();
        animation.OptimizePlus();

        Console.WriteLine("Converting frames {0}", sw.ElapsedMilliseconds);
        foreach (var image in animation)
        {
            image.AnimationDelay = 3;
            image.AnimationIterations = stop ? 1 : -1;
        }
        Console.WriteLine("Converting frames {0}", sw.ElapsedMilliseconds);
        Stream stream = new MemoryStream();

        // Save the animation to a stream
        await animation.WriteAsync(stream, MagickFormat.Gif);
        Console.WriteLine("After saving to stream {0}", sw.ElapsedMilliseconds);
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
