namespace WheelBotApiApp.Services;

public sealed class NoOpWheelBaseImagePrepper : IWheelBaseImagePrepper
{
    public void Enqueue(string wheelId, IReadOnlyList<string> options)
    {
    }
}
