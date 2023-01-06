using Discord;
using Discord.WebSocket;
using System.Diagnostics;
using System.Reactive.Concurrency;
using WheelBot;
using WheelBotApiApp.WheelGenerators;

namespace WheelBotApiApp.Services;

public class DiscordService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly WheelService _wheelService;
    private readonly IWheelGenerator _wheelGenerator;
    private readonly DiscordSocketClient _client;
    private readonly DiscordShardedClient _sharedClient;

    public DiscordService(IConfiguration config, WheelService wheelService, IWheelGenerator wheelGenerator)
    {
        _config = config;
        _wheelService = wheelService;
        _wheelGenerator = wheelGenerator;
        _client = new();
        _sharedClient = new();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _sharedClient.SlashCommandExecuted += Handle;

        Console.WriteLine("trying to login to bot using token of length {0}", _config["BotToken"]?.Length);

        await _client.LoginAsync(TokenType.Bot, _config["BotToken"]);
        await _sharedClient.LoginAsync(TokenType.Bot, _config["BotToken"]);

        _client.Ready += OnReady;

        await _client.StartAsync();
        await _sharedClient.StartAsync();

        Console.WriteLine("Bot is running");
    }

    private async Task OnReady()
    {
        var cmds = await _client.GetGlobalApplicationCommandsAsync();
        //var cmds = new List<SocketApplicationCommand>();
        if (!cmds.Any(c => c.Name == "spin"))
        {
            var command = new SlashCommandBuilder()
                .WithName("spin")
                .WithDescription("Spins the wheel");
            await _client.CreateGlobalApplicationCommandAsync(command.Build());
        }
        if (!cmds.Any(c => c.Name == "add"))
        {
            var command = new SlashCommandBuilder()
                .WithName("add")
                .WithDescription("Adds an option to the wheel")
                .AddOption("option", ApplicationCommandOptionType.String, "The option to add to the wheel", true);
            await _client.CreateGlobalApplicationCommandAsync(command.Build());
        }
        if (!cmds.Any(c => c.Name == "rm"))
        {
            var command = new SlashCommandBuilder()
                .WithName("rm")
                .WithDescription("Removes an option from the wheel")
                .AddOption("index", ApplicationCommandOptionType.Integer, "Removes option at index")
                .AddOption("option", ApplicationCommandOptionType.String, "Removes option matching text");
            await _client.CreateGlobalApplicationCommandAsync(command.Build());
        }
        if (!cmds.Any(c => c.Name == "randomize"))
        {
            var command = new SlashCommandBuilder()
                .WithName("randomize")
                .WithDescription("Randomizes the order of options on the wheel");
            await _client.CreateGlobalApplicationCommandAsync(command.Build());
        }
        if (!cmds.Any(c => c.Name == "preview"))
        {
            var command = new SlashCommandBuilder()
                .WithName("preview")
                .WithDescription("Generates a still image preview of the wheel");
            await _client.CreateGlobalApplicationCommandAsync(command.Build());
        }
        if (!cmds.Any(c => c.Name == "reset"))
        {
            var command = new SlashCommandBuilder()
                .WithName("reset")
                .WithDescription("Clears all options from the wheel");
            await _client.CreateGlobalApplicationCommandAsync(command.Build());
        }
    }

    private async Task Handle(SocketSlashCommand command) => await Task.Run(() => HandleCommandAsync(command));

    private async Task HandleCommandAsync(SocketSlashCommand command)
    {
        Stopwatch stopwatch = new();
        stopwatch.Start();
        try
        {
            await command.DeferAsync();
            var wheel = await _wheelService.GetWheel($"{command.GuildId?.ToString()}_{command.Channel.Name}");
            if (wheel == null)
            {
                await command.FollowupAsync("Could not get or create wheel");
                throw new Exception("Could not get wheel");
            }
            var commandHandlers = new CommandHandlers(_wheelGenerator);

            switch (command.CommandName)
            {
                case "spin":
                    await commandHandlers.HandleSpin(command, wheel);
                    break;
                case "add":
                    await commandHandlers.HandleAdd(command, wheel);
                    break;
                case "rm":
                    await commandHandlers.HandleRemove(command, wheel);
                    break;
                case "randomize":
                    await commandHandlers.HandleRandomize(command, wheel);
                    break;
                case "preview":
                    await commandHandlers.HandlePreveiw(command, wheel);
                    break;
                case "reset":
                    await commandHandlers.HandleReset(command, wheel);
                    break;
                default:
                    await command.FollowupAsync("Unknown command");
                    break;
            }
            await _wheelService.Save($"{command.GuildId?.ToString()}_{command.Channel.Name}", wheel);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            await command.FollowupAsync("Error :(");
            throw;
        }

        Console.WriteLine("Processed command {1} in {0} ms", stopwatch.ElapsedMilliseconds, command.CommandName);
        stopwatch.Stop();
        stopwatch.Reset();
    }
}
