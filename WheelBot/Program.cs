using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using WheelBot;

CommandHandlers commandHandlers = new CommandHandlers();

var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").AddUserSecrets<Program>().Build();

var client = new DiscordSocketClient();

client.SlashCommandExecuted += HandleCommandAsync;

Console.WriteLine("trying to login to bot using token of length {0}", config["BotToken"]?.Length);

await client.LoginAsync(TokenType.Bot, config["BotToken"]);

client.Ready += async () =>
{
    var cmds = await client.GetGlobalApplicationCommandsAsync();
    //var cmds = new List<SocketApplicationCommand>();
    if (!cmds.Any(c => c.Name == "spin"))
    {
        var command = new SlashCommandBuilder()
            .WithName("spin")
            .WithDescription("Spins the wheel");
        await client.CreateGlobalApplicationCommandAsync(command.Build());
    }
    if (!cmds.Any(c => c.Name == "add"))
    {
        var command = new SlashCommandBuilder()
            .WithName("add")
            .WithDescription("Adds an option to the wheel")
            .AddOption("option", ApplicationCommandOptionType.String, "The option to add to the wheel", true);
        await client.CreateGlobalApplicationCommandAsync(command.Build());
    }
    if (!cmds.Any(c => c.Name == "rm"))
    {
        var command = new SlashCommandBuilder()
            .WithName("rm")
            .WithDescription("Removes an option from the wheel")
            .AddOption("index", ApplicationCommandOptionType.Integer, "Removes option at index")
            .AddOption("option", ApplicationCommandOptionType.String, "Removes option matching text");
        await client.CreateGlobalApplicationCommandAsync(command.Build());
    }
    if (!cmds.Any(c => c.Name == "randomize"))
    {
        var command = new SlashCommandBuilder()
            .WithName("randomize")
            .WithDescription("Randomizes the order of options on the wheel");
        await client.CreateGlobalApplicationCommandAsync(command.Build());
    }
    if (!cmds.Any(c => c.Name == "preview"))
    {
        var command = new SlashCommandBuilder()
            .WithName("preview")
            .WithDescription("Generates a still image preview of the wheel");
        await client.CreateGlobalApplicationCommandAsync(command.Build());
    }
};

await client.StartAsync();

Console.WriteLine("Bot is running");

await Task.Delay(-1);


async Task HandleCommandAsync(SocketSlashCommand command)
{
    try
    {
        await command.DeferAsync();
        if (!command.Channel.Name.StartsWith("wheel", StringComparison.InvariantCultureIgnoreCase))
            await command.FollowupAsync("error");
        switch (command.CommandName)
        {
            case "spin":
                await commandHandlers.HandleSpin(command);
                break;
            case "add":
                await commandHandlers.HandleAdd(command);
                break;
            case "rm":
                await commandHandlers.HandleRemove(command);
                break;
            case "randomize":
                await commandHandlers.HandleRandomize(command);
                break;
            case "preview":
                await commandHandlers.HandlePreveiw(command);
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