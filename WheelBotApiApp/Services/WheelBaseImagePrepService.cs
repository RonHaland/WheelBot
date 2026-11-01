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
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _generationSources = new();
    private readonly IWheelAnimationPrecomputer _precomputer;
    private readonly WheelBaseImageStore _store;

    public WheelBaseImagePrepService(IWheelAnimationPrecomputer precomputer, WheelBaseImageStore store)
    {
        _precomputer = precomputer;
        _store = store;
    }

    public void Enqueue(string wheelId, IReadOnlyList<string> options)
    {
        if (options.Count == 0)
        {
            Invalidate(wheelId);
            return;
        }

        var hash = WheelContentHash.Compute(options);
        _latestHashByWheelId[wheelId] = hash;
        var cts = ResetGenerationSource(wheelId);
        _channel.Writer.TryWrite(new PrepJob(wheelId, hash, options.ToList(), cts.Token));
    }

    public void Invalidate(string wheelId)
    {
        _latestHashByWheelId.TryRemove(wheelId, out _);
        CancelGeneration(wheelId);

        _ = Task.Run(async () =>
        {
            try
            {
                await _store.DeletePrefixAsync(wheelId);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Wheel invalidate delete failed for {0}: {1}", wheelId, ex.Message);
            }
        });
    }

    public CancellationToken GetGenerationToken(string wheelId) =>
        _generationSources.GetOrAdd(wheelId, _ => new CancellationTokenSource()).Token;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (!IsLatest(job))
                    continue;

                await _precomputer.PrecomputeAllAsync(job.WheelId, job.Options, job.CancellationToken);

                if (!IsLatest(job))
                    continue;

                await _store.DeleteStaleExceptAsync(job.WheelId, job.ContentHash, stoppingToken);
                Console.WriteLine("Prepped wheel assets for {0} ({1} outcomes)", job.WheelId, job.Options.Count);
            }
            catch (OperationCanceledException) when (job.CancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("Wheel prep cancelled for {0}", job.WheelId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine("Wheel prep failed for {0}: {1}", job.WheelId, ex.Message);
            }
        }
    }

    private CancellationTokenSource ResetGenerationSource(string wheelId)
    {
        CancelGeneration(wheelId);
        var cts = new CancellationTokenSource();
        _generationSources[wheelId] = cts;
        return cts;
    }

    private void CancelGeneration(string wheelId)
    {
        if (_generationSources.TryRemove(wheelId, out var existing))
        {
            try
            {
                existing.Cancel();
            }
            catch
            {
                // ignore
            }

            existing.Dispose();
        }
    }

    private bool IsLatest(PrepJob job) =>
        _latestHashByWheelId.TryGetValue(job.WheelId, out var latest) && latest == job.ContentHash;

    private sealed record PrepJob(string WheelId, string ContentHash, List<string> Options, CancellationToken CancellationToken);
}
