using Discord.WebSocket;
using WheelBotApiApp.WheelGenerators;

namespace WheelBot;

public sealed class CommandHandlers
{
	private readonly IWheelGenerator _wheelGenerator;

	public CommandHandlers(IWheelGenerator wheelGenerator)
	{
        _wheelGenerator = wheelGenerator;
    }

    public async Task HandleSpin(SocketSlashCommand command, Wheel wheel)
	{
		if (!wheel.HasOptions())
		{
			await command.RespondAsync("No options on the wheel, add options using the '/add' command");
			return;
		}

        Console.WriteLine("generated wheel");
        var animation = await _wheelGenerator.GenerateAnimation(wheel);
        await command.FollowupWithFileAsync(animation.SpinningAnimation, "FullAnimation.gif");
		await Task.Delay(7500);
		await command.ModifyOriginalResponseAsync(c => c.Content = $"{command.User.Mention} spun '{animation.Result}'");
		await animation.SpinningAnimation.DisposeAsync();
    }

	public async Task HandleAdd(SocketSlashCommand command, Wheel wheel)
	{
		var optionToAdd = (string)command.Data.Options.First().Value;

        wheel.AddOption(optionToAdd);
		await command.FollowupAsync($"{command.User.Mention} added {optionToAdd} to the wheel!{Environment.NewLine}The full list of options is now ['{string.Join("', '", wheel.Options)}']");
	}

	public async Task HandleRemove(SocketSlashCommand command, Wheel wheel)
	{
        if (!wheel.HasOptions())
        {
            await command.FollowupAsync("No options on the wheel, add options using the '/add' command");
            return;
        }
		if (!command.Data.Options.Any())
		{
			await command.FollowupAsync("Please add a parameter to indicate which option to delete");
			return;
		}
		var option = command.Data.Options.First();
        string removed = string.Empty;
		if (option.Name == "index")
		{
			var value = int.Parse(option.Value.ToString() ?? "");
			if (value < 0 || value > wheel.Options.Count)
			{
				await command.FollowupAsync("index out of range");
				return;
			}

            removed = wheel.RemoveOption(value);
		}
		else
		{
            removed = wheel.RemoveOption(option.Value.ToString() ?? "");
        }

        await command.FollowupAsync($"{command.User.Mention} removed {removed} from the wheel!{Environment.NewLine}The full list of options is now ['{string.Join("', '", wheel.Options)}']");
    }

	public async Task HandleRandomize(SocketSlashCommand command, Wheel wheel)
    {
        if (!wheel.HasOptions())
        {
            await command.FollowupAsync("No options on the wheel, add options using the '/add' command");
            return;
        }

        wheel.RandomizeOrder();
        var stream = await _wheelGenerator.CreatePreview(wheel);

        await command.FollowupWithFileAsync(stream, "preview.png", $"The new order of options is ['{string.Join("', '", wheel.Options)}']");
    }

	public async Task HandlePreveiw(SocketSlashCommand command, Wheel wheel)
    {
        if (!wheel.HasOptions())
        {
            await command.FollowupAsync("No options on the wheel, add options using the '/add' command");
            return;
        }
        var stream = await _wheelGenerator.CreatePreview(wheel);

		await command.FollowupWithFileAsync(stream, "preview.png", $"The full list of options is ['{string.Join("', '", wheel.Options)}']");

    }

    public async Task HandleReset(SocketSlashCommand command, Wheel wheel)
    {
        wheel.Clear(); 
        await command.FollowupAsync($"{command.User.Mention} cleared the wheel!{Environment.NewLine}The full list of options is now ['{string.Join("', '", wheel.Options)}']");
    }
}
