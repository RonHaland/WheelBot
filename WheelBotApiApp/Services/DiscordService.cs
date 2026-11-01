using Discord;
using Discord.WebSocket;
using System.Collections.Concurrent;
using System.Diagnostics;
using WheelBot;
using WheelBotApiApp.WheelGenerators;

namespace WheelBotApiApp.Services;

public class DiscordService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly WheelService _wheelService;
    private readonly IWheelGenerator _wheelGenerator;
    private readonly IWheelBaseImagePrepper _baseImagePrepService;
    private readonly DiscordSocketClient _client;
    private readonly DiscordShardedClient _sharedClient;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _wheelCommandLocks = new();

    public DiscordService(
        IConfiguration config,
        WheelService wheelService,
        IWheelGenerator wheelGenerator,
        IWheelBaseImagePrepper baseImagePrepService)
    {
        _config = config;
        _wheelService = wheelService;
        _wheelGenerator = wheelGenerator;
        _baseImagePrepService = baseImagePrepService;
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

    // Return immediately so Discord.Net can dispatch other interactions while this one runs.
    private Task Handle(SocketSlashCommand command)
    {
        _ = HandleCommandAsync(command);
        return Task.CompletedTask;
    }

    private async Task HandleCommandAsync(SocketSlashCommand command)
    {
        var stopwatch = Stopwatch.StartNew();
        var wheelId = $"{command.GuildId?.ToString()}_{command.Channel.Name}";
        var wheelLock = _wheelCommandLocks.GetOrAdd(wheelId, _ => new SemaphoreSlim(1, 1));

        try
        {
            await command.DeferAsync();
            var commandHandlers = new CommandHandlers(_wheelGenerator, _baseImagePrepService);

            if (command.CommandName is "spin" or "preview")
            {
                Wheel wheelSnapshot;
                await wheelLock.WaitAsync();
                try
                {
                    var wheel = await _wheelService.GetWheel(wheelId);
                    if (wheel == null)
                    {
                        await command.FollowupAsync("Could not get or create wheel");
                        throw new Exception("Could not get wheel");
                    }

                    wheelSnapshot = new Wheel(wheel.Options.ToList());
                    if (command.CommandName == "spin")
                        await _wheelService.Save(wheelId, wheel);
                }
                finally
                {
                    wheelLock.Release();
                }

                if (command.CommandName == "spin")
                    await commandHandlers.HandleSpin(command, wheelId, wheelSnapshot);
                else
                    await commandHandlers.HandlePreveiw(command, wheelId, wheelSnapshot);

                return;
            }

            await wheelLock.WaitAsync();
            try
            {
                var wheel = await _wheelService.GetWheel(wheelId);
                if (wheel == null)
                {
                    await command.FollowupAsync("Could not get or create wheel");
                    throw new Exception("Could not get wheel");
                }

                switch (command.CommandName)
                {
                    case "add":
                        await commandHandlers.HandleAdd(command, wheel);
                        break;
                    case "rm":
                        await commandHandlers.HandleRemove(command, wheel);
                        break;
                    case "randomize":
                        await commandHandlers.HandleRandomize(command, wheelId, wheel);
                        break;
                    case "reset":
                        await commandHandlers.HandleReset(command, wheel);
                        break;
                    default:
                        await command.FollowupAsync("Unknown command");
                        break;
                }
                await _wheelService.Save(wheelId, wheel);

                switch (command.CommandName)
                {
                    case "add":
                    case "rm":
                    case "randomize":
                        _baseImagePrepService.Enqueue(wheelId, wheel.Options.ToList());
                        break;
                    case "reset":
                        _baseImagePrepService.Invalidate(wheelId);
                        break;
                }
            }
            finally
            {
                wheelLock.Release();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            try
            {
                await command.FollowupAsync("Error :(");
            }
            catch (Exception followupEx)
            {
                // Already responded (e.g. file upload succeeded, then a later step failed).
                Console.WriteLine(followupEx);
            }
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine(
                "Processed command {0} for {1} in {2} ms",
                command.CommandName,
                wheelId,
                stopwatch.ElapsedMilliseconds);
        }
    }
}
