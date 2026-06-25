# GHCPSDK Contact Extraction Starter

This workspace contains a starter .NET 10 web API that demonstrates a containerized Azure deployment pattern for:

- downloading a PDF from Azure Blob Storage by filename
- using the GitHub Copilot SDK with a managed-identity-based bearer token against an Azure AI Foundry/OpenAI-compatible endpoint
- extracting contact details from the PDF
- writing the extracted rows to Azure Table Storage

## Prerequisites

- Azure CLI installed and authenticated
- Azure Developer CLI installed
- A GitHub Copilot subscription or BYOK setup for the Copilot SDK
- A Foundry/OpenAI-compatible resource URL and model name

## Deploy with azd

1. Set the Foundry resource URL:
   - PowerShell: $env:FOUNDRY_RESOURCE_URL = "https://<resource>.services.ai.azure.com"
2. Run:
   - azd auth login
   - azd up

The deployment provisions:

- Container Registry
- Container Apps environment
- Container App
- Storage account with Blob and Table support
- User-assigned managed identity and RBAC assignments for Storage

## API

- Health check: GET /health
- Extraction: POST /Extraction with a JSON body like:

```json
{
  "fileName": "specimen_id_01.pdf"
}
```
