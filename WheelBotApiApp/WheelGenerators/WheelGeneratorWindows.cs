using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Color = System.Drawing.Color;
using PointF = System.Drawing.PointF;
using SizeF = System.Drawing.SizeF;

namespace WheelBotApiApp.WheelGenerators;

public class WheelGeneratorWindows : IWheelGenerator
{
    private readonly Color[] _colors = { Color.Red, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Purple };
    private readonly int _size = 360;
    private readonly Random _random = new();

    [SupportedOSPlatform("windows10.0.18362")]
    public async Task<Stream> CreatePreview(Wheel wheel)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception();

        var image = await Task.FromResult(MakeWheel((_size / 2) - 5, wheel));
        MemoryStream stream = new();
        image.Save(stream, ImageFormat.Png);

        return stream;
    }

    [SupportedOSPlatform("windows10.0.18362")]
    public async Task<AnimatedWheel> GenerateAnimation(Wheel wheel)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception();

        var selectedIndex = _random.Next(wheel.Options.Count);

        var rotation = CalculateRotation(selectedIndex, wheel.Options.Count);

        var stream = await CreateAnimation(wheel, 360 * 3 + rotation);

        return new AnimatedWheel(selectedIndex, wheel.Options[selectedIndex], stream);
    }

    int CalculateRotation(int selectedIndex, int length)
    {
        float sweepAngle = 360 / (float)length;
        float targetAngle = sweepAngle * (float)selectedIndex + sweepAngle / 2;
        return (int)(360 - targetAngle);
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private Bitmap MakeWheel(int r, Wheel wheel)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception();

        // Calculate the start and sweep angles for each slice
        int numSlices = wheel.Options.Count;
        float startAngle = 0;
        float sweepAngle = 360 / (float)numSlices;
        var diameter = r * 2;
        var sizeDiff = _size - diameter;
        var halfSizeDiff = sizeDiff / 2;
        var img = new Bitmap(_size, _size);
        var g = Graphics.FromImage(img);

        // Draw the slices of the wheel
        for (int i = 0; i < numSlices; i++)
        {
            // Fill the slice with the appropriate color
            Brush brush = new SolidBrush(_colors[i % _colors.Length]);
            g.FillPie(brush, halfSizeDiff, halfSizeDiff, diameter, diameter, startAngle, sweepAngle);

            // Draw the outline of the slice
            Pen pen = new(Color.Black, 2);
            g.DrawArc(pen, halfSizeDiff, halfSizeDiff, diameter, diameter, startAngle, sweepAngle);
            pen = new(Color.Black, 1);
            g.DrawPie(pen, halfSizeDiff, halfSizeDiff, diameter, diameter, startAngle, sweepAngle);
            // Calculate the size and position of the text
            var fontReduction = wheel.Options[i].Length < 13 ? 28 : (int)(wheel.Options[i].Length * 2.2);
            Font font = new("Verdana", diameter / fontReduction);
            SizeF textSize = g.MeasureString(wheel.Options[i], font);
            float x = halfSizeDiff + diameter / 2 + diameter / 10;
            float y = halfSizeDiff + diameter / 2 - textSize.Height / 2;

            var currentRotation = g.Transform.Clone();

            // Create a rotated Matrix object
            Matrix matrix = g.Transform;
            matrix.RotateAt(startAngle + sweepAngle / 2, new PointF(_size / 2, _size / 2));

            // Set the current transformation matrix to the rotated Matrix
            g.Transform = matrix;

            // Draw the text on the slice
            var textBrush = new SolidBrush(Color.Black);
            g.DrawString(wheel.Options[i], font, textBrush, x, y);

            // Reset the current transformation matrix
            g.Transform = currentRotation;

            // Update the start angle for the next slice
            startAngle += sweepAngle;
        }
        return img;
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private Bitmap MakeSmallTriangle(int h)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception();

        var img = new Bitmap(_size, _size);
        var g = Graphics.FromImage(img);
        var s = _size;

        Pen pen = new(Color.Black, 2);
        Brush brush = new SolidBrush(Color.DarkGray);
        PointF[] points = new[] { new PointF(s-1, s/2 + h/2), new PointF(s-1, s / 2 - h / 2), new PointF(s - h, s / 2) }; 
        g.FillPolygon(brush, points);
        g.DrawPolygon(pen, points);

        return img;
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private async Task<Stream> CreateAnimation(Wheel wheel, int rotation)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception();

        var image = MakeWheel(_size / 2 - 5, wheel);
        var pointer = MakeSmallTriangle(12);
        var gif = CreateGifWithMetadata(image, pointer);

        for (int i = 180; i < rotation; i += int.Max(1, (int)(20 * Factor(i, rotation + 40))))
        {
            Image<Rgba32> frame = CreateFrameFromImage(image, i, pointer);
            gif.Frames.AddFrame(frame.Frames.RootFrame);
        }

        var g2 = Graphics.FromImage(image);
        g2.Transform.RotateAt(rotation, new PointF(_size / 2, _size / 2));
        Image<Rgba32> finalFrame = CreateFrameFromImage(image, rotation, pointer);
        gif.Frames.AddFrame(finalFrame.Frames.RootFrame);

        Stream stream = new MemoryStream();
        await gif.SaveAsGifAsync(stream);
        return stream;
    }

    private double Factor(float x, float fullRotation) => ((fullRotation - (1/fullRotation) * float.Pow(x, 2))/ fullRotation);

    [SupportedOSPlatform("windows10.0.18362")]
    private Image<Rgba32> CreateFrameFromImage(Bitmap image, float rotation, Bitmap pointer)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception();

        Bitmap clone = new(_size, _size);
        Matrix transform = new();
        transform.RotateAt(rotation, new PointF(_size / 2, _size / 2));

        using var g = Graphics.FromImage(clone);
        g.Transform = transform;
        g.DrawImage(image, 0, 0);

        transform.RotateAt(-rotation, new PointF(_size / 2, _size / 2));
        g.Transform = transform;
        g.DrawImage(pointer, 0, 0);

        transform.RotateAt(rotation, new PointF(_size / 2, _size / 2));
        g.Transform = transform;

        var frame = SixLabors.ImageSharp.Image.Load<Rgba32>(ImageToBytes(clone));
        var frameMetadata = frame.Frames.RootFrame.Metadata.GetGifMetadata();
        frameMetadata.FrameDelay = 3;
        return frame;
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private Image<Rgba32> CreateGifWithMetadata(Bitmap initialFrame, Bitmap pointer)
    {
        Image<Rgba32> gif = CreateFrameFromImage(initialFrame, 0, pointer);
        var gifMetadata = gif.Metadata.GetGifMetadata();
        gifMetadata.RepeatCount = 1;

        var frameMetadata = gif.Frames.RootFrame.Metadata.GetGifMetadata();
        frameMetadata.FrameDelay = 8;

        return gif;
    }

    [SupportedOSPlatform("windows10.0.18362")]
    static byte[]? ImageToBytes(System.Drawing.Image img)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception();

        ImageConverter converter = new();
        return (byte[]?)converter.ConvertTo(img, typeof(byte[]));
    }
}
