using System.Security.Cryptography;
using System.Text;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace WheelBotApiApp.Services;

public sealed class WheelBaseImageStore
{
    private const string ContainerName = "wheel-bases";
    private readonly BlobContainerClient _container;
    private readonly StorageSharedKeyCredential _credential;

    public WheelBaseImageStore(IConfiguration configuration)
    {
        var connectionString = configuration["WheelConnStr"]
            ?? throw new InvalidOperationException("WheelConnStr is not configured.");
        _credential = ParseSharedKeyCredential(connectionString);
        var service = new BlobServiceClient(connectionString);
        _container = service.GetBlobContainerClient(ContainerName);
        _container.CreateIfNotExists(PublicAccessType.None);
    }

    public async Task<byte[]?> TryDownloadAsync(string wheelId, string contentHash, CancellationToken cancellationToken = default)
    {
        var blob = BlobClientFor(BlobPath(wheelId, contentHash));
        if (!await blob.ExistsAsync(cancellationToken))
            return null;

        var response = await blob.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToArray();
    }

    public async Task UploadAsync(string wheelId, string contentHash, byte[] pngBytes, CancellationToken cancellationToken = default)
    {
        var blob = BlobClientFor(BlobPath(wheelId, contentHash));
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
        await foreach (var item in _container.GetBlobsAsync(new GetBlobsOptions { Prefix = prefix }, cancellationToken))
        {
            if (string.Equals(item.Name, keepBlobName, StringComparison.Ordinal))
                continue;

            await BlobClientFor(item.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
    }

    public async Task DeletePrefixAsync(string wheelId, CancellationToken cancellationToken = default)
    {
        var prefix = $"{HashWheelId(wheelId)}/";
        await foreach (var item in _container.GetBlobsAsync(new GetBlobsOptions { Prefix = prefix }, cancellationToken))
        {
            await BlobClientFor(item.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Azure.Storage.Blobs' GetBlobClient rebuilds the URI and drops the container name when the
    /// endpoint host is not an IP and the port is outside the SDK path-style whitelist
    /// (10000-10004, 10100-10104, …). Our Azurite listens on 10500 as host "wheelazurite",
    /// so we must build BlobClient from the container URI explicitly.
    /// </summary>
    private BlobClient BlobClientFor(string blobName)
    {
        var uri = new Uri($"{_container.Uri.AbsoluteUri.TrimEnd('/')}/{blobName.TrimStart('/')}");
        return new BlobClient(uri, _credential);
    }

    private static StorageSharedKeyCredential ParseSharedKeyCredential(string connectionString)
    {
        // Matches the well-known Azurite / UseDevelopmentStorage account.
        if (connectionString.Contains("UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
        {
            return new StorageSharedKeyCredential(
                "devstoreaccount1",
                "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==");
        }

        var parts = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);

        if (!parts.TryGetValue("AccountName", out var accountName) ||
            !parts.TryGetValue("AccountKey", out var accountKey))
        {
            throw new InvalidOperationException(
                "WheelConnStr must include AccountName and AccountKey for blob access.");
        }

        return new StorageSharedKeyCredential(accountName, accountKey);
    }

    private static string BlobPath(string wheelId, string contentHash) =>
        $"{HashWheelId(wheelId)}/{contentHash}.png";

    private static string HashWheelId(string wheelId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(wheelId ?? string.Empty));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
