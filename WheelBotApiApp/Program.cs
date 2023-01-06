using System.Runtime.InteropServices;
using WheelBot;
using WheelBotApiApp.Services;
using WheelBotApiApp.WheelGenerators;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<DiscordService>();
builder.Services.AddSingleton<WheelService>();
builder.Services.AddSingleton<CommandHandlers>();
builder.Services.AddSingleton<IWheelGenerator>(sp => 
    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
        ? new WheelGeneratorWindows() 
        : new WheelGeneratorMultiPlatform());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Run();
