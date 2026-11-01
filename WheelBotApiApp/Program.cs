using System.Runtime.InteropServices;
using WheelBot;
using WheelBotApiApp.Services;
using WheelBotApiApp.WheelGenerators;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<WheelService>();
builder.Services.AddSingleton<WheelBaseImageStore>();
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    builder.Services.AddSingleton<IWheelGenerator, WheelGeneratorWindows>();
    builder.Services.AddSingleton<IWheelBaseImagePrepper, NoOpWheelBaseImagePrepper>();
}
else
{
    builder.Services.AddSingleton<WheelGeneratorMultiPlatform>();
    builder.Services.AddSingleton<IWheelGenerator>(sp => sp.GetRequiredService<WheelGeneratorMultiPlatform>());
    builder.Services.AddSingleton<IWheelBaseRenderer>(sp => sp.GetRequiredService<WheelGeneratorMultiPlatform>());
    builder.Services.AddSingleton<IWheelAnimationPrecomputer>(sp => sp.GetRequiredService<WheelGeneratorMultiPlatform>());
    builder.Services.AddSingleton<WheelBaseImagePrepService>();
    builder.Services.AddSingleton<IWheelBaseImagePrepper>(sp => sp.GetRequiredService<WheelBaseImagePrepService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<WheelBaseImagePrepService>());
}

builder.Services.AddHostedService<DiscordService>();

builder.Build().Run();
