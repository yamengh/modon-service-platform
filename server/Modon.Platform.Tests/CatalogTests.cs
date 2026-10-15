using System.Net.Http.Json;
using System.Text.Json;
using Modon.Platform.Catalog;

namespace Modon.Platform.Tests;

[Collection("Modon API")]
public class CatalogTests(ModonAppFactory factory)
{
    private const string ListUrl = "/api/queries/modonServices";
    private static string DetailUrl(string code) => $"/api/queries/modonService/single?code={code}";

    private async Task<JsonElement[]> ListServices(string url = ListUrl)
    {
        var res = await factory.NewClient().GetAsync(url);
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {body}");
        return JsonSerializer.Deserialize<JsonElement[]>(body)!;
    }

    [Fact]
    public async Task Anonymous_CanListTheTwoSeededServices_InSortOrder()
    {
        var services = await ListServices();

        Assert.Equal(
            [ServiceCatalogData.BuildingLicenceCode, ServiceCatalogData.ExcavationPermitCode],
            services.Select(s => s.GetProperty("code").GetString()));
        Assert.Equal("تصريح أعمال الحفر", services[1].GetProperty("nameAr").GetString());
        Assert.Equal("contractor", services[1].GetProperty("beneficiary").GetString());
    }

    [Fact]
    public async Task Anonymous_CanGetServiceDetail_ByCode()
    {
        var detail = await factory.NewClient()
            .GetFromJsonAsync<JsonElement>(DetailUrl(ServiceCatalogData.ExcavationPermitCode));

        Assert.Equal("Excavation Works Permit", detail.GetProperty("nameEn").GetString());
        Assert.Equal("3 working days", detail.GetProperty("durationEn").GetString());
        Assert.Contains("Traffic safety plan", detail.GetProperty("requiredDocumentsEn").GetString());
        Assert.Equal(3, detail.GetProperty("stepsAr").GetString()!.Split('\n').Length);
    }

    [Fact]
    public async Task UnknownServiceCode_ReturnsNoService()
    {
        var res = await factory.NewClient().GetAsync(DetailUrl("does-not-exist"));

        // FormCMS returns 200 with an empty body when a single query matches nothing.
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode && (body == "" || body == "null"),
            $"Expected an empty result, got {(int)res.StatusCode}: {body}");
    }

    [Fact]
    public async Task Investor_CannotCreateCatalogueEntries()
    {
        var client = await factory.LoginAs("investor@modon.local");

        var res = await client.PostAsJsonAsync($"/api/entities/{ServiceCatalogSchema.EntityName}/insert", new
        {
            code = "fake-service", nameAr = "x", nameEn = "x", summaryAr = "x", summaryEn = "x",
            beneficiary = "investor", sortOrder = 99
        });

        Assert.False(res.IsSuccessStatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CanManageCatalogueThroughFormCms()
    {
        var client = await factory.LoginAs("sadmin@modon.local");

        var res = await client.GetAsync($"/api/entities/{ServiceCatalogSchema.EntityName}");

        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Seeding_IsIdempotent()
    {
        await CatalogSeeder.SeedAsync(factory.Services);

        var services = await ListServices($"{ListUrl}?nocache={Guid.NewGuid():N}");
        Assert.Equal(2, services.Length);
    }
}
