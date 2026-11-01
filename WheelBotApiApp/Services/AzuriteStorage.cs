using Azure.Storage.Blobs;

namespace WheelBotApiApp.Services;

internal static class AzuriteStorage
{
    /// <summary>
    /// Azurite validates x-ms-version and often lags the default version in Azure.Storage.Blobs.
    /// Use a version Azurite 3.35+ supports when talking to the dev store account.
    /// </summary>
    public static BlobClientOptions CreateBlobClientOptions(string? connectionString)
    {
        if (!IsEmulatorConnection(connectionString))
            return new BlobClientOptions();

        return new BlobClientOptions(BlobClientOptions.ServiceVersion.V2025_11_05);
    }

    public static bool IsEmulatorConnection(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        return connectionString.Contains("UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("devstoreaccount1", StringComparison.OrdinalIgnoreCase);
    }
}
