namespace WheelBotApiApp.WheelGenerators;

public class Wheel
{
    private readonly List<string> _options = new();
    private readonly Random _random = new();
    public bool HasOptions() => _options.Any();
    public List<string> Options => _options;

    public Wheel(List<string> options)
    {
        _options = options;
    }

    public Wheel() { }

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

    public string RemoveOption(int value)
    {
        var removed = _options[value];
        _options.RemoveAt(value);
        return removed;
    }

    public string RemoveOption(string value)
    {
        _options.RemoveAll(x => x == value);
        return value;
    }

    public void Clear()
    {
        _options.Clear();
    }
}
