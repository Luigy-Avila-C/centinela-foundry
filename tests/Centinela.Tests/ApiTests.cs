using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Centinela.Api;
using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Centinela.Tests;

public sealed class ApiFixture : WebApplicationFactory<Program>
{
    public const string Key = "clave-de-prueba";
    public InMemoryCaseRepository Repository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Api:Key"] = Key }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICaseRepository>();
            services.AddSingleton<ICaseRepository>(Repository);
        });
    }

    public HttpClient Client(string? key = Key)
    {
        var client = CreateClient();
        if (key is not null) client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    public async Task<Guid> SeedAsync(ComplianceCase c)
    {
        await Repository.SaveAsync(c, default);
        return c.Id;
    }
}

internal static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(T)).ToList()) services.Remove(d);
    }
}

public class ApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(null)]
    [InlineData("otra")]
    public async Task Every_api_call_needs_the_right_key(string? key)
    {
        var response = await api.Client(key).GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_panel_page_and_its_security_headers_are_served_without_a_key()
    {
        var response = await api.Client(null).GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("Panel de aprobación", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_panel_does_not_use_inline_scripts_or_innerhtml()
    {
        var html = await api.Client(null).GetStringAsync("/");
        var js = await api.Client(null).GetStringAsync("/app.js");

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("innerHTML", js);
    }

    [Fact]
    public async Task The_list_shows_pending_cases_with_their_warnings()
    {
        var id = await api.SeedAsync(CaseFixtures.AwaitingApproval(auditPasses: false, pending: true));

        var list = await api.Client().GetFromJsonAsync<JsonElement>("/api/cases?status=AwaitingHumanApproval", Json);

        var mine = list.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == id);
        Assert.True(mine.GetProperty("escalated").GetBoolean());
        Assert.Equal(1, mine.GetProperty("pendingData").GetInt32());
    }

    [Fact]
    public async Task An_unknown_status_filter_is_a_bad_request()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client().GetAsync("/api/cases?status=Inventado")).StatusCode);
    }

    [Fact]
    public async Task The_detail_includes_the_cited_norm_text_and_the_risks()
    {
        var id = await api.SeedAsync(CaseFixtures.AwaitingApproval(auditPasses: false));

        var detail = await api.Client().GetFromJsonAsync<JsonElement>($"/api/cases/{id}", Json);

        var citation = detail.GetProperty("actions")[0].GetProperty("citations")[0];
        Assert.Equal("Texto del artículo 1.", citation.GetProperty("text").GetString());
        Assert.True(detail.GetProperty("canDecide").GetBoolean());
        Assert.NotEmpty(detail.GetProperty("risksToAcknowledge").EnumerateArray());
    }

    [Fact]
    public async Task A_missing_case_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client().GetAsync($"/api/cases/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task A_clean_case_is_approved_and_the_decision_is_persisted()
    {
        var id = await api.SeedAsync(CaseFixtures.AwaitingApproval());

        var response = await api.Client().PostAsJsonAsync($"/api/cases/{id}/approve", new { reviewer = "Ana" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await api.Repository.GetAsync(id, default);
        Assert.Equal(CaseStatus.Approved, stored!.Status);
        Assert.Contains("Aprobado por Ana", stored.Log[^1]);
    }

    [Fact]
    public async Task Risky_approval_without_acknowledgement_is_422_and_changes_nothing()
    {
        var id = await api.SeedAsync(CaseFixtures.AwaitingApproval(auditPasses: false));

        var response = await api.Client().PostAsJsonAsync($"/api/cases/{id}/approve", new { reviewer = "Ana", acknowledgeRisks = false });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(CaseStatus.AwaitingHumanApproval, (await api.Repository.GetAsync(id, default))!.Status);
    }

    [Fact]
    public async Task Risky_approval_with_acknowledgement_goes_through()
    {
        var id = await api.SeedAsync(CaseFixtures.AwaitingApproval(auditPasses: false));

        var response = await api.Client().PostAsJsonAsync($"/api/cases/{id}/approve", new { reviewer = "Ana", acknowledgeRisks = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Approval_without_a_reviewer_is_a_bad_request()
    {
        var id = await api.SeedAsync(CaseFixtures.AwaitingApproval());

        var response = await api.Client().PostAsJsonAsync($"/api/cases/{id}/approve", new { reviewer = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rejection_needs_a_comment_and_records_it()
    {
        var id = await api.SeedAsync(CaseFixtures.AwaitingApproval());

        var withoutComment = await api.Client().PostAsJsonAsync($"/api/cases/{id}/reject", new { reviewer = "Ana", comment = " " });
        var ok = await api.Client().PostAsJsonAsync($"/api/cases/{id}/reject", new { reviewer = "Ana", comment = "No procede" });

        Assert.Equal(HttpStatusCode.BadRequest, withoutComment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var stored = await api.Repository.GetAsync(id, default);
        Assert.Equal((CaseStatus.Rejected, "No procede"), (stored!.Status, stored.ReviewerComment));
    }

    [Fact]
    public async Task A_case_that_is_already_decided_answers_409()
    {
        var id = await api.SeedAsync(CaseFixtures.AwaitingApproval());
        await api.Client().PostAsJsonAsync($"/api/cases/{id}/approve", new { reviewer = "Ana" });

        var again = await api.Client().PostAsJsonAsync($"/api/cases/{id}/reject", new { reviewer = "Luis", comment = "tarde" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_case_that_is_not_awaiting_approval_cannot_be_approved()
    {
        var c = new ComplianceCase(CaseFixtures.Change(withSections: false));
        var id = await api.SeedAsync(c);

        var response = await api.Client().PostAsJsonAsync($"/api/cases/{id}/approve", new { reviewer = "Ana", acknowledgeRisks = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}

public class ApiStartupTests
{
    [Fact]
    public void The_api_refuses_to_start_in_production_without_a_key()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Production"));

        var e = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Api:Key", e.ToString());
    }
}

public sealed class DemoFixture : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Enabled"] = "true" }));
    }
}

public class DemoModeTests(DemoFixture demo) : IClassFixture<DemoFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_demo_loads_the_real_example_cases_and_the_panel_can_log_in_by_itself()
    {
        var client = demo.CreateClient();

        var info = await client.GetFromJsonAsync<JsonElement>("/demo.json", Json);
        client.DefaultRequestHeaders.Add("X-Api-Key", info.GetProperty("apiKey").GetString());
        var cases = await client.GetFromJsonAsync<JsonElement>("/api/cases", Json);

        Assert.True(info.GetProperty("demo").GetBoolean());
        Assert.Equal(3, cases.GetArrayLength());
        Assert.Equal(2, cases.EnumerateArray().Count(c => c.GetProperty("status").GetString() == "AwaitingHumanApproval"));
        Assert.Contains(cases.EnumerateArray(), c => c.GetProperty("status").GetString() == "Approved");
    }

    [Fact]
    public async Task A_demo_case_shows_the_original_the_proposal_and_the_measured_consumption()
    {
        var client = demo.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", DemoMode.Key);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/cases?status=AwaitingHumanApproval", Json);
        var big = list.EnumerateArray().OrderByDescending(c => c.GetProperty("actions").GetInt32()).First();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/cases/{big.GetProperty("id").GetGuid()}", Json);

        Assert.NotEmpty(detail.GetProperty("actions").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("actions")[0].GetProperty("original").GetString()));
        Assert.True(detail.GetProperty("estimatedUsd").GetDecimal() > 0);
    }

    [Fact]
    public async Task Demo_decisions_work_but_only_in_memory_and_still_demand_acknowledging_risks()
    {
        // Instancia propia: esta prueba cambia el estado de un caso y no debe afectar a las demás.
        using var own = new DemoFixture();
        var client = own.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", DemoMode.Key);
        var escalated = (await client.GetFromJsonAsync<JsonElement>("/api/cases?status=AwaitingHumanApproval", Json))
            .EnumerateArray().First(c => c.GetProperty("escalated").GetBoolean());
        var id = escalated.GetProperty("id").GetGuid();

        var without = await client.PostAsJsonAsync($"/api/cases/{id}/approve", new { reviewer = "Visitante" });
        var with = await client.PostAsJsonAsync($"/api/cases/{id}/approve", new { reviewer = "Visitante", acknowledgeRisks = true });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, without.StatusCode);
        Assert.Equal(HttpStatusCode.OK, with.StatusCode);
    }

    [Fact]
    public async Task Without_demo_mode_the_demo_endpoint_does_not_exist()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Api:Key"] = "clave" }));
        });

        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/demo.json")).StatusCode);
    }

    [Fact]
    public void The_demo_refuses_to_start_if_a_real_cosmos_is_configured()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Demo:Enabled"] = "true",
                ["Cosmos:Endpoint"] = "https://ejemplo.documents.azure.com:443/",
            }));
        });

        var e = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Demo:Enabled", e.ToString());
    }
}
