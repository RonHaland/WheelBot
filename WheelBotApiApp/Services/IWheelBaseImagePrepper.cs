namespace WheelBotApiApp.Services;

public interface IWheelBaseImagePrepper
{
    void Enqueue(string wheelId, IReadOnlyList<string> options);
}
