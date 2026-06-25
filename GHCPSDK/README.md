# GHCPSDK Contact Extraction Starter

This repository contains a .NET 10 ASP.NET Core API that demonstrates a containerized Azure pattern for:

- downloading a document from Azure Blob Storage by filename
- authenticating with Azure Identity
- sending the file to a GitHub Copilot SDK agent running against an Azure AI Foundry/OpenAI-compatible endpoint
- extracting structured contact details from the document
- returning the extracted contacts over an HTTP API

> The current implementation intentionally skips Azure Table Storage writes by default so the core validation path is blob download + LLM extraction. The service can still be configured to persist to Table Storage later.

## What is in this repo

- [src/ContactExtraction.Api](src/ContactExtraction.Api): the API project
- [src/ContactExtraction.Api/Controllers/ExtractionController.cs](src/ContactExtraction.Api/Controllers/ExtractionController.cs): HTTP endpoint for extraction
- [src/ContactExtraction.Api/Services/AzureStorageService.cs](src/ContactExtraction.Api/Services/AzureStorageService.cs): Blob download and optional Table write logic
- [src/ContactExtraction.Api/Services/CopilotExtractionService.cs](src/ContactExtraction.Api/Services/CopilotExtractionService.cs): Foundry authentication and Copilot agent execution
- [scripts/Invoke-ContactExtraction.ps1](scripts/Invoke-ContactExtraction.ps1): helper script to call the endpoint locally
- [scripts/Run-ContactExtractionDocker.ps1](scripts/Run-ContactExtractionDocker.ps1): helper script to build and run the container with a service principal
- [tests/ContactExtraction.Api.Tests](tests/ContactExtraction.Api.Tests): unit tests for the helper logic

## Prerequisites

Before rebuilding this locally, make sure you have:

- .NET 10 SDK and runtime
- Docker Desktop (for container validation)
- Azure CLI (recommended for local development auth)
- An Azure subscription
- An Azure Storage account with a Blob container containing the input file
- An Azure AI Foundry or OpenAI-compatible resource endpoint
- A deployment name for the model you want to use
- An identity that can access both Blob Storage and the Foundry endpoint

## Azure setup

You will need these Azure resources and permissions:

1. Storage account
   - Create a storage account
   - Create a blob container, for example `pdf`
   - Upload a sample file such as `contact_ppt.pptx` or another supported document into that container
   - Grant your identity `Storage Blob Data Contributor` on the storage account or container scope

2. Foundry / Azure AI endpoint
   - Create or reuse an Azure AI Foundry/OpenAI-compatible resource
   - Create or select a model deployment (for example `gpt-5.4-mini`)
   - Grant your identity the required access to the Foundry resource

3. Authentication
   - For local development, run `az login`
   - For containers, either:
     - pass a service principal via environment variables, or
     - mount a credential source into the container

## Configuration

The application reads configuration from:

- [src/ContactExtraction.Api/appsettings.json](src/ContactExtraction.Api/appsettings.json)
- [src/ContactExtraction.Api/appsettings.Development.json](src/ContactExtraction.Api/appsettings.Development.json)
- environment variables

The most important settings are:

- `AZURE_STORAGE_ACCOUNT_NAME`
- `AZURE_STORAGE_CONTAINER_NAME`
- `FOUNDRY_RESOURCE_URL`
- `FOUNDRY_DEPLOYMENT_NAME` or `FOUNDRY_MODEL_NAME`
- `FOUNDRY_WIRE_API` (defaults to `responses`)
- `SKIP_TABLE_STORAGE` (set to `true` to skip table persistence)

Example PowerShell configuration:

```powershell
$env:AZURE_STORAGE_ACCOUNT_NAME="<storage-account-name>"
$env:AZURE_STORAGE_CONTAINER_NAME="pdf"
$env:FOUNDRY_RESOURCE_URL="https://<resource>.services.ai.azure.com/openai/v1"
$env:FOUNDRY_DEPLOYMENT_NAME="gpt-5.4-mini"
$env:FOUNDRY_WIRE_API="responses"
$env:SKIP_TABLE_STORAGE="true"
```

If you use a service principal inside Docker, also set:

```powershell
$env:AZURE_TENANT_ID="<tenant-id>"
$env:AZURE_CLIENT_ID="<client-id>"
$env:AZURE_CLIENT_SECRET="<client-secret>"
```

## Run the service in Docker first

The easiest way to validate the full flow is to start the API in Docker and then invoke it with the PowerShell helper script.

### 1) Build the container image

```powershell
docker build -f src/ContactExtraction.Api/Dockerfile -t contact-extraction-api .
```

### 2) Run the container with the required environment variables

```powershell
docker run -d --name contact-extraction-api -p 5111:8080 `
  -e ASPNETCORE_URLS=http://+:8080 `
  -e ASPNETCORE_ENVIRONMENT=Production `
  -e AZURE_STORAGE_ACCOUNT_NAME="<storage-account-name>" `
  -e AZURE_STORAGE_CONTAINER_NAME="pdf" `
  -e FOUNDRY_RESOURCE_URL="https://<resource>.services.ai.azure.com/openai/v1" `
  -e FOUNDRY_DEPLOYMENT_NAME="gpt-5.4-mini" `
  -e FOUNDRY_WIRE_API="responses" `
  -e SKIP_TABLE_STORAGE="true" `
  -e AZURE_TENANT_ID="<tenant-id>" `
  -e AZURE_CLIENT_ID="<client-id>" `
  -e AZURE_CLIENT_SECRET="<client-secret>" `
  contact-extraction-api
```

### 3) Verify the service is up

```powershell
Invoke-RestMethod http://localhost:5111/health
```

You should receive a JSON response with the status `all is well`.

### 4) Run the PowerShell helper to trigger extraction

The repository includes a helper script that sends the request to the running service:

```powershell
cd scripts
./Invoke-ContactExtraction.ps1 -FileName 'contact_ppt.pptx'
```

This script posts to `http://localhost:5111/Extraction` with the filename you provide. The filename must exactly match the blob name in Azure Storage.

If you prefer, you can also use the Docker helper script to start the container automatically:

```powershell
./scripts/Run-ContactExtractionDocker.ps1
```

## Build and run locally

If you want to run the API directly on your machine instead of in Docker, use:

```powershell
dotnet restore

dotnet test tests/ContactExtraction.Api.Tests/ContactExtraction.Api.Tests.csproj
```

Run the API locally:

```powershell
dotnet run --project src/ContactExtraction.Api --urls http://localhost:5111
```

Then run the same PowerShell helper:

```powershell
cd scripts
./Invoke-ContactExtraction.ps1 -FileName 'contact_ppt.pptx'
```

## Expected behavior

Once everything is configured correctly:

1. The API receives a filename.
2. It downloads the matching blob from Azure Storage.
3. It authenticates with Azure Identity.
4. It starts the Copilot CLI agent runtime.
5. The agent writes the input file into its sandbox and extracts contacts.
6. The API returns the extracted contacts as JSON.

## Troubleshooting

- If the request fails with an authentication error, verify that the current identity can access both Blob Storage and the Foundry resource.
- If the container cannot authenticate, make sure the container has a valid credential source such as a service principal environment block, an attached managed identity, or a mounted Azure credential file.
- If the file is not found in Blob Storage, verify the container name and the exact blob name.
- If the LLM call fails, confirm the Foundry endpoint and deployment name are correct.

## Notes

- The current starter focuses on validating the extraction path rather than table persistence.
- If you later want to re-enable Azure Table Storage writes, remove or change `SKIP_TABLE_STORAGE` and ensure the identity has the appropriate Table permissions.

