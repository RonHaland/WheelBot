namespace WheelBotApiApp.WheelGenerators
{
    public interface IWheelGenerator
    {
        Task<AnimatedWheel> GenerateAnimation(Wheel wheel);
        Task<Stream> CreatePreview(Wheel wheel);
    }
}
