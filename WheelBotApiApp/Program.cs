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
builder.Services.AddSingleton<IWheelGenerator>(sp => 
    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
        ? new WheelGeneratorWindows() 
        : new WheelGeneratorMultiPlatform());

builder.Build().Run();
