namespace WheelBotApiApp.Services;

public interface IWheelBaseImagePrepper
{
    void Enqueue(string wheelId, IReadOnlyList<string> options);

    void Invalidate(string wheelId);

    CancellationToken GetGenerationToken(string wheelId);
}
