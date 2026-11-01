namespace WheelBotApiApp.WheelGenerators
{
    public interface IWheelGenerator
    {
        Task<AnimatedWheel> GenerateAnimation(string wheelId, Wheel wheel, CancellationToken cancellationToken = default);
        Task<Stream> CreatePreview(string wheelId, Wheel wheel);
    }
}
