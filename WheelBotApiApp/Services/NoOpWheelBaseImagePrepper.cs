namespace WheelBotApiApp.Services;

public sealed class NoOpWheelBaseImagePrepper : IWheelBaseImagePrepper
{
    public void Enqueue(string wheelId, IReadOnlyList<string> options)
    {
    }

    public void Invalidate(string wheelId)
    {
    }

    public CancellationToken GetGenerationToken(string wheelId) => CancellationToken.None;
}
