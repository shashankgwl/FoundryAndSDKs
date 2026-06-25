using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using ContactExtraction.Api.Models;
using GitHub.Copilot;

namespace ContactExtraction.Api.Services;

public sealed class CopilotExtractionService : IAsyncDisposable
{
    // Microsoft Foundry's OpenAI-compatible endpoint accepts Microsoft Entra ID
    // bearer tokens issued for this scope (NOT the cognitiveservices scope).
    private const string FoundryTokenScope = "https://ai.azure.com/.default";

    private readonly IConfiguration _configuration;
    private readonly ILogger<CopilotExtractionService> _logger;
    private readonly DefaultAzureCredential _credential;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private CopilotClient? _client;

    public CopilotExtractionService(IConfiguration configuration, ILogger<CopilotExtractionService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _credential = new DefaultAzureCredential();
    }

    public async Task<IReadOnlyList<ContactRecord>> ExtractContactsAsync(Stream pdfStream, string fileName, CancellationToken cancellationToken)
    {
        var foundryEndpoint = _configuration["FOUNDRY_RESOURCE_URL"]
            ?? _configuration["FOUNDRY_ENDPOINT"]
            ?? throw new InvalidOperationException("FOUNDRY_RESOURCE_URL or FOUNDRY_ENDPOINT is required.");
        var deploymentName = _configuration["FOUNDRY_DEPLOYMENT_NAME"]
            ?? _configuration["FOUNDRY_MODEL_NAME"]
            ?? throw new InvalidOperationException("FOUNDRY_DEPLOYMENT_NAME or FOUNDRY_MODEL_NAME is required.");

        var baseUrl = BuildOpenAiBaseUrl(foundryEndpoint);

        // "responses" suits GPT-5/reasoning models; "completions" suits chat models
        // such as Grok that only advertise chat_completion. Override via FOUNDRY_WIRE_API.
        var wireApi = _configuration["FOUNDRY_WIRE_API"] ?? "responses";

        // Use a short-lived Entra bearer token via DefaultAzureCredential
        // (managed identity, service principal, or az login).
        var provider = new ProviderConfig
        {
            Type = "openai",
            BaseUrl = baseUrl,
            WireApi = wireApi,
        };

        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(new[] { FoundryTokenScope }),
            cancellationToken);
        provider.BearerToken = token.Token;

        var (workDir, pdfPath) = await WritePdfToWorkdirAsync(pdfStream, fileName, cancellationToken);

        var client = await GetClientAsync(cancellationToken);

        try
        {
            // The agent runs inside this working directory as its sandbox: it has a real
            // shell and can write and run its own scripts (Python/PowerShell/shell) against
            // the PDF, charting its own path until it has extracted the contacts.
            await using var session = await client.CreateSessionAsync(new SessionConfig
            {
                Model = deploymentName,
                WorkingDirectory = workDir,
                // Let the agent use its shell / file tools without blocking on prompts.
                OnPermissionRequest = PermissionHandler.ApproveAll,
                Provider = provider,
            }, cancellationToken);

            // Agentic runs write and execute code, so allow far more than the 60s default.
            AssistantMessageEvent? response = await session.SendAndWaitAsync(
                new MessageOptions { Prompt = BuildPrompt(pdfPath) },
                timeout: TimeSpan.FromMinutes(10),
                cancellationToken: cancellationToken);

            var assistantContent = response?.Data.Content ?? string.Empty;
            var contacts = ParseContacts(assistantContent);

            return contacts.Select(contact =>
            {
                var contactId = NormalizeGuid(contact.Id);
                return new ContactRecord
                {
                    RowKey = contactId.ToString("D"),
                    FileName = fileName,
                    Source = "CopilotExtraction",
                    Name = string.IsNullOrWhiteSpace(contact.Name) ? "Unknown" : contact.Name.Trim(),
                    DateOfBirth = ParseDateOfBirth(contact.DateOfBirth),
                    Gender = string.IsNullOrWhiteSpace(contact.Gender) ? string.Empty : contact.Gender.Trim(),
                    Id = contactId,
                };
            }).ToList();
        }
        finally
        {
            TryCleanupWorkdir(workDir);
        }
    }

    /// <summary>
    /// Builds the OpenAI-compatible base URL the Copilot SDK BYOK provider expects, e.g.
    /// <c>https://my-resource.cognitiveservices.azure.com/openai/v1/</c>.
    /// </summary>
    public static string BuildOpenAiBaseUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Foundry endpoint is required.", nameof(endpoint));
        }

        var normalizedEndpoint = endpoint.Trim().TrimEnd('/');

        // Strip a trailing chat/completions path if a full URL was provided.
        if (normalizedEndpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            normalizedEndpoint = normalizedEndpoint[..^"/chat/completions".Length];
        }

        if (normalizedEndpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedEndpoint + "/";
        }

        if (normalizedEndpoint.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedEndpoint + "/v1/";
        }

        return normalizedEndpoint + "/openai/v1/";
    }

    private async Task<CopilotClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (_client is null)
            {
                _logger.LogInformation("Starting Copilot SDK runtime.");
                var client = new CopilotClient();
                await client.StartAsync(cancellationToken);
                _client = client;
            }
        }
        finally
        {
            _startGate.Release();
        }

        return _client;
    }

    private async Task<(string WorkDir, string PdfPath)> WritePdfToWorkdirAsync(Stream pdfStream, string fileName, CancellationToken cancellationToken)
    {
        // Each request gets an isolated sandbox folder. In the container this defaults to
        // /work (a writable dir baked into the image); locally it falls back to the temp dir.
        var workRoot = _configuration["AGENT_WORK_ROOT"]
            ?? Path.Combine(Path.GetTempPath(), "contact-extraction");

        var workDir = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "document.pdf";
        }

        var pdfPath = Path.Combine(workDir, safeName);
        await using (var file = File.Create(pdfPath))
        {
            await pdfStream.CopyToAsync(file, cancellationToken);
        }

        _logger.LogInformation("Wrote PDF to agent sandbox {PdfPath}", pdfPath);
        return (workDir, pdfPath);
    }

    private void TryCleanupWorkdir(string workDir)
    {
        try
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up agent sandbox {WorkDir}", workDir);
        }
    }

    private static string BuildPrompt(string pdfPath)
    {
        var fileName = Path.GetFileName(pdfPath);
        var prompt = new StringBuilder();
        prompt.AppendLine("You are an autonomous contact-extraction agent running inside a Linux sandbox.");
        prompt.AppendLine($"A PDF file named '{fileName}' is in your current working directory.");
        prompt.AppendLine("You have a real shell and may write and run any code you need (Python, PowerShell, or shell) to read it.");
        prompt.AppendLine("Chart your own path. For example: use pdftotext/pdfplumber/pypdf to pull embedded text, and fall back to OCR (e.g. tesseract) when the PDF is scanned images. Install any packages you need.");
        prompt.AppendLine("Iterate until you have reliably extracted every contact in the document.");
        prompt.AppendLine();
        prompt.AppendLine("Goal: extract all contact records. For each contact capture Name, DateOfBirth, Gender, and Id.");
        prompt.AppendLine();
        prompt.AppendLine("When finished, output ONLY a JSON array of objects with exactly these fields: Name, DateOfBirth, Gender, Id.");
        prompt.AppendLine("Final-answer rules:");
        prompt.AppendLine("- Use null where data is unavailable.");
        prompt.AppendLine("- DateOfBirth must be an ISO 8601 date string such as 1990-05-12, or null.");
        prompt.AppendLine("- Id must be a GUID string if available, otherwise null.");
        prompt.AppendLine("- Do NOT wrap the JSON in markdown fences and do NOT add any prose before or after it.");
        return prompt.ToString();
    }

    private static IReadOnlyList<ContactResult> ParseContacts(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<ContactResult>();
        }

        var normalizedContent = NormalizeContent(content);

        try
        {
            var document = JsonSerializer.Deserialize<List<ContactResult>>(normalizedContent, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return document ?? new List<ContactResult>();
        }
        catch
        {
            return new List<ContactResult> { new() { Name = "Unknown", DateOfBirth = null, Gender = string.Empty, Id = null } };
        }
    }

    private static string NormalizeContent(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak > 0)
            {
                trimmed = trimmed[(firstLineBreak + 1)..];
            }

            var closingFence = trimmed.LastIndexOf("```");
            if (closingFence > 0)
            {
                trimmed = trimmed[..closingFence];
            }
        }

        return trimmed.Trim();
    }

    private static Guid NormalizeGuid(string? value)
    {
        return Guid.TryParse(value, out var parsedGuid)
            ? parsedGuid
            : Guid.NewGuid();
    }

    private static DateTimeOffset? ParseDateOfBirth(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsedDate)
            ? parsedDate
            : DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsedDateTime)
                ? new DateTimeOffset(parsedDateTime)
                : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        _startGate.Dispose();
    }

    private sealed class ContactResult
    {
        public string? Name { get; set; }
        public string? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Id { get; set; }
    }
}
