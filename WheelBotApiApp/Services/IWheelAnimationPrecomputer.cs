namespace WheelBotApiApp.Services;

public interface IWheelAnimationPrecomputer
{
    Task PrecomputeAllAsync(string wheelId, IReadOnlyList<string> options, CancellationToken cancellationToken);
}
