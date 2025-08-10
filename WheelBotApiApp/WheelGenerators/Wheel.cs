namespace WheelBotApiApp.WheelGenerators;

public class Wheel
{
    private readonly Random _random = new();
    public bool HasOptions() => Options.Count != 0;
    public List<string> Options { get; } = [];

    public Wheel(List<string> options)
    {
        Options = options;
    }

    public Wheel() { }

    public void RandomizeOrder()
    {
        var newOrder = Options.OrderBy(x => _random.Next(5000)).ToList();
        Options.Clear();
        Options.AddRange(newOrder);
    }

    public void AddOption(string option)
    {
        Options.Add(option);
    }

    public string RemoveOption(int value)
    {
        var removed = Options[value];
        Options.RemoveAt(value);
        return removed;
    }

    public string RemoveOption(string value)
    {
        Options.RemoveAll(x => x == value);
        return value;
    }

    public void Clear()
    {
        Options.Clear();
    }
}
