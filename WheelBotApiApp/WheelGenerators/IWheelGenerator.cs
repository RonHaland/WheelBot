namespace WheelBotApiApp.WheelGenerators
{
    public interface IWheelGenerator
    {
        Task<AnimatedWheel> GenerateAnimation(string wheelId, Wheel wheel);
        Task<Stream> CreatePreview(string wheelId, Wheel wheel);
    }
}
