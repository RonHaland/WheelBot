using Discord.WebSocket;
using WheelBotApiApp.Services;
using WheelBotApiApp.WheelGenerators;

namespace WheelBot;

public sealed class CommandHandlers
{
	private readonly IWheelGenerator _wheelGenerator;
	private readonly IWheelBaseImagePrepper _baseImagePrepper;

	public CommandHandlers(IWheelGenerator wheelGenerator, IWheelBaseImagePrepper baseImagePrepper)
	{
        _wheelGenerator = wheelGenerator;
        _baseImagePrepper = baseImagePrepper;
    }

    public async Task HandleSpin(SocketSlashCommand command, string wheelId, Wheel wheel)
	{
		if (!wheel.HasOptions())
		{
			await command.FollowupAsync("No options on the wheel, add options using the '/add' command");
			return;
		}

		try
		{
			var cancellationToken = _baseImagePrepper.GetGenerationToken(wheelId);
			var animation = await _wheelGenerator.GenerateAnimation(wheelId, wheel, cancellationToken);
			await command.FollowupWithFileAsync(animation.SpinningAnimation, "FullAnimation.webp");
			await Task.Delay(7500, cancellationToken);
			await command.ModifyOriginalResponseAsync(c => c.Content = $"{command.User.Mention} spun '{animation.Result}'");
			await animation.SpinningAnimation.DisposeAsync();
		}
		catch (OperationCanceledException)
		{
			await command.FollowupAsync("Spin cancelled because the wheel options changed.");
		}
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
			if (value < 0 || value >= wheel.Options.Count)
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

	public async Task HandleRandomize(SocketSlashCommand command, string wheelId, Wheel wheel)
    {
        if (!wheel.HasOptions())
        {
            await command.FollowupAsync("No options on the wheel, add options using the '/add' command");
            return;
        }

        wheel.RandomizeOrder();
        await using var stream = await _wheelGenerator.CreatePreview(wheelId, wheel);

        await command.FollowupWithFileAsync(stream, "preview.png", $"The new order of options is ['{string.Join("', '", wheel.Options)}']");
    }

	public async Task HandlePreveiw(SocketSlashCommand command, string wheelId, Wheel wheel)
    {
        if (!wheel.HasOptions())
        {
            await command.FollowupAsync("No options on the wheel, add options using the '/add' command");
            return;
        }
        await using var stream = await _wheelGenerator.CreatePreview(wheelId, wheel);

		await command.FollowupWithFileAsync(stream, "preview.png", $"The full list of options is ['{string.Join("', '", wheel.Options)}']");

    }

    public async Task HandleReset(SocketSlashCommand command, Wheel wheel)
    {
        wheel.Clear(); 
        await command.FollowupAsync($"{command.User.Mention} cleared the wheel!{Environment.NewLine}The full list of options is now ['{string.Join("', '", wheel.Options)}']");
    }
}
