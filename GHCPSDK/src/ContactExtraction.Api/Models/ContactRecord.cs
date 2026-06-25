using Azure;
using Azure.Data.Tables;

namespace ContactExtraction.Api.Models;

public sealed class ContactRecord : ITableEntity
{
    public string PartitionKey { get; set; } = "contacts";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset? DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public Guid Id { get; set; } = Guid.NewGuid();
}
