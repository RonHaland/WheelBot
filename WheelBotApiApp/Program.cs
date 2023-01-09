using System.Runtime.InteropServices;
using WheelBot;
using WheelBotApiApp.Services;
using WheelBotApiApp.WheelGenerators;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<DiscordService>();
builder.Services.AddSingleton<WheelService>();
builder.Services.AddSingleton<CommandHandlers>();
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    builder.Services.AddSingleton<IWheelGenerator, WheelGeneratorWindows>();
else
    builder.Services.AddSingleton<IWheelGenerator, WheelGeneratorMultiPlatform>();

builder.Build().Run();
