using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using ContactExtraction.Api.Models;

namespace ContactExtraction.Api.Services;

public sealed class AzureStorageService
{
    private readonly BlobContainerClient _blobContainerClient;
    private readonly TableClient _tableClient;

    public AzureStorageService(IConfiguration configuration)
    {
        var accountName = configuration["AZURE_STORAGE_ACCOUNT_NAME"] ?? throw new InvalidOperationException("AZURE_STORAGE_ACCOUNT_NAME is required.");
        var containerName = configuration["AZURE_STORAGE_CONTAINER_NAME"] ?? throw new InvalidOperationException("AZURE_STORAGE_CONTAINER_NAME is required.");
        var tableName = configuration["AZURE_TABLE_NAME"] ?? "contacts";

        if (string.IsNullOrWhiteSpace(accountName) || accountName.Contains("<your-storage-account-name>", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("AZURE_STORAGE_ACCOUNT_NAME must be set to a real Azure Storage account name.");
        }

        var credential = new DefaultAzureCredential();
        var blobUri = $"https://{accountName}.blob.core.windows.net";
        var tableUri = $"https://{accountName}.table.core.windows.net";

        _blobContainerClient = new BlobContainerClient(new Uri($"{blobUri}/{containerName}"), credential, new BlobClientOptions());
        _tableClient = new TableClient(new Uri(tableUri), tableName, credential, new TableClientOptions());
    }

    public async Task<Stream> DownloadPdfAsync(string fileName, CancellationToken cancellationToken)
    {
        var blobClient = _blobContainerClient.GetBlobClient(fileName);
        var stream = new MemoryStream();
        await blobClient.DownloadToAsync(stream, cancellationToken);
        stream.Position = 0;
        return stream;
    }

    public async Task UpsertContactsAsync(IEnumerable<ContactRecord> contacts, CancellationToken cancellationToken)
    {
        await _tableClient.CreateIfNotExistsAsync(cancellationToken);

        var batch = contacts.Select(contact => new TableTransactionAction(TableTransactionActionType.UpsertMerge, contact));
        await _tableClient.SubmitTransactionAsync(batch, cancellationToken);
    }
}
