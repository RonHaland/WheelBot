namespace WheelBotApiApp.WheelGenerators;

public interface IWheelBaseRenderer
{
    byte[] RenderBaseWheelPng(Wheel wheel);
}
