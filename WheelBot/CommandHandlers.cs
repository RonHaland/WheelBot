using Discord.WebSocket;
using Newtonsoft.Json.Linq;

namespace WheelBot;

public sealed class CommandHandlers
{
	private readonly WheelGenerator _wheel;
	public CommandHandlers()
	{
		_wheel = new WheelGenerator();
		_wheel.AddOption("opt 1");
        _wheel.AddOption("opt 2");
        _wheel.AddOption("opt 3");
        _wheel.AddOption("opt 4");
    }

	public async Task HandleSpin(SocketSlashCommand command)
	{
		if (!_wheel.HasOptions())
		{
			await command.RespondAsync("No options on the wheel, add options using the '/add' command");
			return;
		}

        Console.WriteLine("generated wheel");
        var animation = await _wheel.GenerateAnimation();
        await command.FollowupWithFileAsync(animation.SpinningAnimation, "FullAnimation.gif");
		await Task.Delay(6000);
		Console.WriteLine("modifying original message");
		await command.ModifyOriginalResponseAsync(c => c.Content = animation.Result);
		animation.SpinningAnimation.Close();
    }

	public async Task HandleAdd(SocketSlashCommand command)
	{
		var optionToAdd = (string)command.Data.Options.First().Value;
		
        _wheel.AddOption(optionToAdd);
		await command.FollowupAsync($"{command.User.Username} added {optionToAdd} to the wheel!{Environment.NewLine}The full list of options is now ['{string.Join("', '", _wheel.Options)}']");
	}
	public async Task HandleRemove(SocketSlashCommand command)
	{
        if (!_wheel.HasOptions())
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
		if (option.Name == "index")
		{
			var value = int.Parse(option.Value.ToString() ?? "");
			if (value < 0 || value > _wheel.Options.Count)
			{
				await command.FollowupAsync("index out of range");
				return;
			}

            var removed = _wheel.RemoveOption(value);
            await command.FollowupAsync($"{command.User.Username} removed {removed} from the wheel!{Environment.NewLine}The full list of options is now ['{string.Join("', '", _wheel.Options)}']");
		}
		else
		{
            var removed = _wheel.RemoveOption(option.Value.ToString() ?? "");
            await command.FollowupAsync($"{command.User.Username} removed {removed} from the wheel!{Environment.NewLine}The full list of options is now ['{string.Join("', '", _wheel.Options)}']");
        }

    }

	public async Task HandleRandomize(SocketSlashCommand command)
    {
        if (!_wheel.HasOptions())
        {
            await command.FollowupAsync("No options on the wheel, add options using the '/add' command");
            return;
        }

        _wheel.RandomizeOrder();
        var stream = _wheel.CreatePreview();

        await command.FollowupWithFileAsync(stream, "preview.png", $"The new order of options is ['{string.Join("', '", _wheel.Options)}']");
    }

	public async Task HandlePreveiw(SocketSlashCommand command)
    {
        if (!_wheel.HasOptions())
        {
            await command.FollowupAsync("No options on the wheel, add options using the '/add' command");
            return;
        }
        var stream = _wheel.CreatePreview();

		await command.FollowupWithFileAsync(stream, "preview.png", $"The full list of options is ['{string.Join("', '", _wheel.Options)}']");

    }
}
