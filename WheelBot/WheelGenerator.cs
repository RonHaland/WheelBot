using ImageMagick;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace WheelBot;

public sealed class WheelGenerator
{
    private readonly Color[] _colors = { Color.Red, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Purple };
    private readonly List<string> _options = new();
    private readonly Random _random = new Random();
    public async Task<string> GenerateAnimations()
    {
        if (_options.Count <= 0)
            return "No Options :O";

        var selectedIndex = _random.Next(_options.Count);

        var rotation = CalculateRotation(selectedIndex, _options.Count);

        await CreateAnimation(600, _colors, _options, "FullAnimation.webp");
        await CreateAnimation(600, _colors, _options, "SelectedOption.webp", 360 + rotation, true);

        return _options[selectedIndex];

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

    static IEnumerable<byte[]> ImagesToBytes(IEnumerable<Image> imgs)
    {
        ImageConverter converter = new ImageConverter();
        return imgs.Select(i => (byte[])converter.ConvertTo(i, typeof(byte[])));
    }

    static async Task CreateAnimation(int size, Color[] colors, IList<string> options, string filename, int rotation = 360, bool stop = false)
    {
        Bitmap bmp = new Bitmap(size, size);

        // Create a new Graphics object to draw on the Bitmap
        Graphics g = Graphics.FromImage(bmp);
        List<Bitmap> frames = new();

        // Loop to rotate the wheel and save each frame of the animation
        for (int i = 0; i < rotation; i += 5)
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
        }

        // Create an empty list of MagickImage objects
        var images = new List<MagickImage>();

        // Convert the bitmaps to MagickImage objects and add them to the list
        foreach (var frame in ImagesToBytes(frames))
        {
            var image = new MagickImage(frame);
            image.Alpha(AlphaOption.Discrete);
            images.Add(image);
        }

        // Create an animation from the list of images
        var animation = new MagickImageCollection(images);

        // Set the animation delay to 100ms
        animation.Coalesce();
        foreach (var image in animation)
        {
            image.AnimationDelay = 2;
            image.AnimationIterations = stop ? 1 : -1;
        }

        // Save the animation to a webp file
        await animation.WriteAsync(filename);
    }
}
