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
    private readonly BlobClientOptions _blobClientOptions;

    public WheelBaseImageStore(IConfiguration configuration)
    {
        var connectionString = configuration["WheelConnStr"]
            ?? throw new InvalidOperationException("WheelConnStr is not configured.");
        _credential = ParseSharedKeyCredential(connectionString);
        _blobClientOptions = AzuriteStorage.CreateBlobClientOptions(connectionString);
        var service = new BlobServiceClient(connectionString, _blobClientOptions);
        _container = service.GetBlobContainerClient(ContainerName);
        _container.CreateIfNotExists(PublicAccessType.None);
    }

    public Task<byte[]?> TryDownloadBaseAsync(string wheelId, string contentHash, CancellationToken cancellationToken = default) =>
        TryDownloadBlobAsync(BaseBlobPath(wheelId, contentHash), cancellationToken);

    public Task UploadBaseAsync(string wheelId, string contentHash, byte[] pngBytes, CancellationToken cancellationToken = default) =>
        UploadBlobAsync(BaseBlobPath(wheelId, contentHash), pngBytes, "image/png", cancellationToken);

    public Task<byte[]?> TryDownloadSpinAsync(string wheelId, string contentHash, int outcomeIndex, CancellationToken cancellationToken = default) =>
        TryDownloadBlobAsync(SpinBlobPath(wheelId, contentHash, outcomeIndex), cancellationToken);

    public Task UploadSpinAsync(string wheelId, string contentHash, int outcomeIndex, byte[] webpBytes, CancellationToken cancellationToken = default) =>
        UploadBlobAsync(SpinBlobPath(wheelId, contentHash, outcomeIndex), webpBytes, "image/webp", cancellationToken);

    public async Task DeleteStaleExceptAsync(string wheelId, string currentContentHash, CancellationToken cancellationToken = default)
    {
        var wheelPrefix = $"{HashWheelId(wheelId)}/";
        var keepPrefix = ContentPrefix(wheelId, currentContentHash);
        await foreach (var item in _container.GetBlobsAsync(new GetBlobsOptions { Prefix = wheelPrefix }, cancellationToken))
        {
            if (item.Name.StartsWith(keepPrefix, StringComparison.Ordinal))
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

    private async Task<byte[]?> TryDownloadBlobAsync(string blobPath, CancellationToken cancellationToken)
    {
        var blob = BlobClientFor(blobPath);
        if (!await blob.ExistsAsync(cancellationToken))
            return null;

        var response = await blob.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToArray();
    }

    private async Task UploadBlobAsync(string blobPath, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        var blob = BlobClientFor(blobPath);
        await using var stream = new MemoryStream(bytes);
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, cancellationToken);
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
        return new BlobClient(uri, _credential, _blobClientOptions);
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

    private static string ContentPrefix(string wheelId, string contentHash) =>
        $"{HashWheelId(wheelId)}/{contentHash}/";

    private static string BaseBlobPath(string wheelId, string contentHash) =>
        $"{ContentPrefix(wheelId, contentHash)}base.png";

    // Bump when spin encode semantics change so cached WebPs from older pipelines are ignored.
    private const string SpinBlobVersion = "v2";

    private static string SpinBlobPath(string wheelId, string contentHash, int outcomeIndex) =>
        $"{ContentPrefix(wheelId, contentHash)}spin-{SpinBlobVersion}-{outcomeIndex}.webp";

    private static string HashWheelId(string wheelId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(wheelId ?? string.Empty));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
