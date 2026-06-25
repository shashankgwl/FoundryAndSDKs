using ContactExtraction.Api.Services;

namespace ContactExtraction.Api.Tests;

public sealed class CopilotExtractionServiceTests
{
    [Fact]
    public void BuildOpenAiBaseUrl_AppendsOpenAiV1()
    {
        var uri = CopilotExtractionService.BuildOpenAiBaseUrl("https://example.cognitiveservices.azure.com/");

        Assert.Equal("https://example.cognitiveservices.azure.com/openai/v1/", uri);
    }

    [Fact]
    public void BuildOpenAiBaseUrl_NormalizesChatCompletionsPath()
    {
        var uri = CopilotExtractionService.BuildOpenAiBaseUrl("https://example.cognitiveservices.azure.com/openai/v1/chat/completions");

        Assert.Equal("https://example.cognitiveservices.azure.com/openai/v1/", uri);
    }
}
