using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using WheelBot;

//var generator = new WheelGenerator();
//generator.AddOption("CHug");
//generator.AddOption("Chris");
//generator.AddOption("Del ut 2");
//generator.AddOption("Gutta drikker");
//generator.AddOption("Drikk 1");
//generator.RandomizeOrder();
//var result = await generator.GenerateAnimation();

//Console.WriteLine(result);

CommandHandlers commandHandlers = new CommandHandlers();

var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

var client = new DiscordSocketClient();

client.SlashCommandExecuted += HandleCommandAsync;

await client.LoginAsync(TokenType.Bot, config["BotToken"]);

client.Ready += async () =>
{
    //var cmds = await client.GetGlobalApplicationCommandsAsync();
    var cmds = new List<SocketApplicationCommand>();
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
            .WithDescription("Removes an option from the wheel");
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

await Task.Delay(-1);


async Task HandleCommandAsync(SocketSlashCommand command)
{
    if (!command.Channel.Name.StartsWith("wheel", StringComparison.InvariantCultureIgnoreCase))
        await command.RespondAsync("error");
    switch (command.CommandName)
    {
        case "spin":
            await commandHandlers.HandleSpin(command);
            break;
        case "add":
            await commandHandlers.HandleAdd(command);
            break;
        case "rm":
            break;
        case "randomize":
            break;
        case "preview":
            await commandHandlers.HandlePreveiw(command);
            break;
        default:
            await command.RespondAsync("error");
            break;
    }
    if (command.CommandName.StartsWith("spin"))
    {
        await command.RespondWithFileAsync("FullAnimation.gif", "FullAnimation.gif");
    }
    await command.RespondAsync("Error :(");
}