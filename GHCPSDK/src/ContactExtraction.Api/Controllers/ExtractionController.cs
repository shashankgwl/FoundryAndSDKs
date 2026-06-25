using ContactExtraction.Api.Models;
using ContactExtraction.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactExtraction.Api.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class ExtractionController : ControllerBase
{
    private readonly AzureStorageService _storageService;
    private readonly CopilotExtractionService _copilotService;

    public ExtractionController(AzureStorageService storageService, CopilotExtractionService copilotService)
    {
        _storageService = storageService;
        _copilotService = copilotService;
    }

    [HttpPost]
    public async Task<IActionResult> Extract([FromBody] ExtractionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return BadRequest(new { error = "FileName is required." });
        }

        try
        {
            await using var pdfStream = await _storageService.DownloadPdfAsync(request.FileName, cancellationToken);
            var contacts = await _copilotService.ExtractContactsAsync(pdfStream, request.FileName, cancellationToken);

            // Persisting to Table storage is optional for now; don't let a table-write
            // failure mask the extraction result we actually want to inspect.
            string? storageWarning = null;
            try
            {
                await _storageService.UpsertContactsAsync(contacts, cancellationToken);
            }
            catch (Exception storageEx)
            {
                storageWarning = storageEx.Message;
            }

            return Ok(new { fileName = request.FileName, count = contacts.Count, contacts, storageWarning });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
