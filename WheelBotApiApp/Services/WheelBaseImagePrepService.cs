using System.Collections.Concurrent;
using System.Threading.Channels;
using WheelBotApiApp.WheelGenerators;

namespace WheelBotApiApp.Services;

public sealed class WheelBaseImagePrepService : BackgroundService, IWheelBaseImagePrepper
{
    private readonly Channel<PrepJob> _channel = Channel.CreateUnbounded<PrepJob>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly ConcurrentDictionary<string, string> _latestHashByWheelId = new();
    private readonly IWheelBaseRenderer _renderer;
    private readonly WheelBaseImageStore _store;

    public WheelBaseImagePrepService(IWheelBaseRenderer renderer, WheelBaseImageStore store)
    {
        _renderer = renderer;
        _store = store;
    }

    public void Enqueue(string wheelId, IReadOnlyList<string> options)
    {
        if (options.Count == 0)
            return;

        var hash = WheelContentHash.Compute(options);
        _latestHashByWheelId[wheelId] = hash;
        _channel.Writer.TryWrite(new PrepJob(wheelId, hash, options.ToList()));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (!_latestHashByWheelId.TryGetValue(job.WheelId, out var latest) || latest != job.ContentHash)
                    continue;

                if (await _store.TryDownloadAsync(job.WheelId, job.ContentHash, stoppingToken) is not null)
                    continue;

                var wheel = new Wheel(job.Options);
                var pngBytes = _renderer.RenderBaseWheelPng(wheel);
                await _store.UploadAsync(job.WheelId, job.ContentHash, pngBytes, stoppingToken);
                Console.WriteLine("Prepped wheel base blob for {0} ({1} bytes)", job.WheelId, pngBytes.Length);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine("Wheel base prep failed for {0}: {1}", job.WheelId, ex.Message);
            }
        }
    }

    private sealed record PrepJob(string WheelId, string ContentHash, List<string> Options);
}
