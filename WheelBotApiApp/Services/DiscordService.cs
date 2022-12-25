using Discord;
using Discord.WebSocket;
using WheelBot;

namespace WheelBotApiApp.Services;

public class DiscordService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _client;
    private CommandHandlers _commandHandlers;

    public DiscordService(IConfiguration config)
    {
        _commandHandlers = new();
        _config = config;
        _client = new();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {


        _client.SlashCommandExecuted += HandleCommandAsync;

        Console.WriteLine("trying to login to bot using token of length {0}", _config["BotToken"]?.Length);

        await _client.LoginAsync(TokenType.Bot, _config["BotToken"]);

        _client.Ready += OnReady;

        await _client.StartAsync();

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
    }

    private async Task HandleCommandAsync(SocketSlashCommand command)
    {
        try
        {
            await command.DeferAsync();
            if (!command.Channel.Name.StartsWith("wheel", StringComparison.InvariantCultureIgnoreCase))
                await command.FollowupAsync("error");
            switch (command.CommandName)
            {
                case "spin":
                    await _commandHandlers.HandleSpin(command);
                    break;
                case "add":
                    await _commandHandlers.HandleAdd(command);
                    break;
                case "rm":
                    await _commandHandlers.HandleRemove(command);
                    break;
                case "randomize":
                    await _commandHandlers.HandleRandomize(command);
                    break;
                case "preview":
                    await _commandHandlers.HandlePreveiw(command);
                    break;
                default:
                    await command.FollowupAsync("error");
                    break;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            await command.FollowupAsync("Error :(");
            throw;
        }
    }
}
