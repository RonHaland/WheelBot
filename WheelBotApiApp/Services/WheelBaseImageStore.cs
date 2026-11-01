using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace WheelBotApiApp.Services;

public sealed class WheelBaseImageStore
{
    private const string ContainerName = "wheel-bases";
    private readonly BlobContainerClient _container;

    public WheelBaseImageStore(IConfiguration configuration)
    {
        var connectionString = configuration["WheelConnStr"]
            ?? throw new InvalidOperationException("WheelConnStr is not configured.");
        var service = new BlobServiceClient(connectionString);
        _container = service.GetBlobContainerClient(ContainerName);
        _container.CreateIfNotExists(PublicAccessType.None);
    }

    public async Task<byte[]?> TryDownloadAsync(string wheelId, string contentHash, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(BlobPath(wheelId, contentHash));
        if (!await blob.ExistsAsync(cancellationToken))
            return null;

        var response = await blob.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToArray();
    }

    public async Task UploadAsync(string wheelId, string contentHash, byte[] pngBytes, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(BlobPath(wheelId, contentHash));
        await using var stream = new MemoryStream(pngBytes);
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "image/png" },
        }, cancellationToken);
    }

    public async Task DeleteStaleExceptAsync(string wheelId, string currentContentHash, CancellationToken cancellationToken = default)
    {
        var prefix = $"{HashWheelId(wheelId)}/";
        var keepBlobName = BlobPath(wheelId, currentContentHash);
        await foreach (var item in _container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            if (string.Equals(item.Name, keepBlobName, StringComparison.Ordinal))
                continue;

            await _container.DeleteBlobIfExistsAsync(item.Name, cancellationToken: cancellationToken);
        }
    }

    public async Task DeletePrefixAsync(string wheelId, CancellationToken cancellationToken = default)
    {
        var prefix = $"{HashWheelId(wheelId)}/";
        await foreach (var item in _container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            await _container.DeleteBlobIfExistsAsync(item.Name, cancellationToken: cancellationToken);
        }
    }

    private static string BlobPath(string wheelId, string contentHash) =>
        $"{HashWheelId(wheelId)}/{contentHash}.png";

    private static string HashWheelId(string wheelId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(wheelId ?? string.Empty));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
