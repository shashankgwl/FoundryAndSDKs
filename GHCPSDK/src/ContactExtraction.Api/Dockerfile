FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# --- Agent runtime toolchain -------------------------------------------------
# The Copilot SDK spawns the `copilot` CLI, which runs the autonomous agent loop.
# Give it Node.js (for the CLI), Python + common PDF/OCR tooling, and a writable
# sandbox so it can write and run its own extraction scripts.
ENV PIP_BREAK_SYSTEM_PACKAGES=1 \
    AGENT_WORK_ROOT=/work
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates curl gnupg git \
        python3 python3-pip python3-venv \
        poppler-utils tesseract-ocr \
    && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && npm install -g @github/copilot \
    && pip3 install --no-cache-dir pdfplumber pypdf pytesseract pillow \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /work && chmod 777 /work

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/ContactExtraction.Api/ContactExtraction.Api.csproj", "src/ContactExtraction.Api/"]
RUN dotnet restore "src/ContactExtraction.Api/ContactExtraction.Api.csproj"
COPY . .
WORKDIR /src/src/ContactExtraction.Api
RUN dotnet publish "ContactExtraction.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ContactExtraction.Api.dll"]
