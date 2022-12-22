using Discord.WebSocket;

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

		Console.WriteLine("starting generation");
		await command.DeferAsync();
        var animation = await _wheel.GenerateAnimation();
		Console.WriteLine("generated wheel");
        await command.FollowupWithFileAsync(animation.SpinningAnimation, "FullAnimation.gif");
		await Task.Delay(8000);
		Console.WriteLine("modifying original message");
		await command.ModifyOriginalResponseAsync(c => c.Content = animation.Result);
		animation.SpinningAnimation.Close();
    }

	public async Task HandleAdd(SocketSlashCommand command)
	{
		var optionToAdd = (string)command.Data.Options.First().Value;
		
        _wheel.AddOption(optionToAdd);
		await command.RespondAsync($"{command.User.Username} added {optionToAdd} to the wheel!{Environment.NewLine}The full list of options is now ['{string.Join("', '", _wheel.Options)}']");
	}

	public async Task HandlePreveiw(SocketSlashCommand command)
    {
        if (!_wheel.HasOptions())
        {
            await command.RespondAsync("No options on the wheel, add options using the '/add' command");
            return;
        }
        var stream = _wheel.CreatePreview();

		await command.RespondWithFileAsync(stream, "preview.png", $"The full list of options is ['{string.Join("', '", _wheel.Options)}']");

    }
}
