using Azure;
using Azure.Data.Tables;
using Discord;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using WheelBot;

namespace WheelBotApiApp.Services;

public class WheelService
{
    private readonly TableClient _client;
    private MemoryCache _wheelCache = new(Options.Create(new MemoryCacheOptions { }));
	public WheelService(IConfiguration config)
	{
		var client = new TableClient(config["wheelConnStr"], "wheel");
		client.CreateIfNotExists();
		_client = client;
	}

	public async Task<WheelGenerator?> GetWheel(string wheelId)
	{
		if (string.IsNullOrWhiteSpace(wheelId))
			throw new ArgumentNullException(nameof(wheelId), "give a wheel Id");

		if (_wheelCache.TryGetValue(wheelId, out WheelGenerator? wheel))
			return wheel;

		var entity = await _client.GetEntityIfExistsAsync<WheelEntity>("wheel", wheelId);
        List<string> options = new();
        if (entity.HasValue) 
		{
			options.AddRange(JsonSerializer.Deserialize<List<string>>(entity.Value.OptionsJson) ?? new List<string>());
		}
        var newWheel = new WheelGenerator(options);
        _wheelCache.Set(wheelId, newWheel);

        return newWheel;
    }

	public async Task Save(string wheelId, WheelGenerator wheelGen)
	{
		var wheel = new WheelEntity("wheel", wheelId, DateTime.UtcNow, new ETag()) { OptionsJson = JsonSerializer.Serialize(wheelGen.Options) };
		await _client.UpsertEntityAsync(wheel);
	}
}

internal class WheelEntity : ITableEntity
{
	public WheelEntity(string partitionKey, string rowKey, DateTimeOffset? timestamp, ETag eTag)
    {
        PartitionKey = partitionKey;
        RowKey = rowKey;
        Timestamp = timestamp;
        ETag = eTag;

        OptionsJson = string.Empty;
    }

	public WheelEntity()
	{
		PartitionKey = string.Empty;
		RowKey = string.Empty;
		ETag = new ETag("");

		OptionsJson = string.Empty;
    }

	public string OptionsJson { get; set; }

	public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
