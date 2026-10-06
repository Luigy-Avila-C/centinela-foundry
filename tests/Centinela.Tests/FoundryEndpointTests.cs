using Centinela.Infrastructure.Foundry;

namespace Centinela.Tests;

public class FoundryEndpointTests
{
    [Fact]
    public void Derives_the_account_openai_endpoint_from_the_project_endpoint()
    {
        // Los embeddings dan 404 por el endpoint del proyecto; solo funcionan por el de la cuenta.
        var derived = FoundryProject.DeriveOpenAiEndpoint(
            new Uri("https://foundry-ejemplo.services.ai.azure.com/api/projects/centinela"));

        Assert.Equal("https://foundry-ejemplo.openai.azure.com/openai/v1/", derived.ToString());
    }

    [Fact]
    public void Index_dates_are_read_whether_they_arrive_as_text_or_as_a_date()
    {
        // Regresión: SearchDocument entregaba un string y la lectura lanzaba InvalidCastException.
        var expected = new DateOnly(2026, 10, 5);

        Assert.Equal(expected, Centinela.Infrastructure.Search.AzureSearchChunkIndex.ParseDate("2026-10-05T00:00:00Z"));
        Assert.Equal(expected, Centinela.Infrastructure.Search.AzureSearchChunkIndex.ParseDate(
            new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero)));
        Assert.Throws<InvalidOperationException>(() =>
            Centinela.Infrastructure.Search.AzureSearchChunkIndex.ParseDate(null));
    }

    [Fact]
    public void Refuses_to_guess_when_the_host_is_not_a_foundry_host()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FoundryProject.DeriveOpenAiEndpoint(new Uri("https://otro-host.example.com/api/projects/x")));
    }
}
