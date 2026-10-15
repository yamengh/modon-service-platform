using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Modon.Platform.Requests;

namespace Modon.Platform.Tests;

/// <summary>Shared helpers for request and attachment tests.</summary>
public static class RequestTestKit
{
    public const string RequestsUrl = "/api/modon/requests";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static readonly string[] ExcavationSlots =
        ServiceApplications.ExcavationPermit.Slots.Select(s => s.Code).ToArray();

    // Minimal files whose first bytes match the real format signatures.
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    public static readonly byte[] Pdf = "%PDF-1.4\n%test document\n"u8.ToArray();

    /// <summary>A complete, valid excavation form (codes are seeded lookup values).</summary>
    public static ExcavationDto ValidExcavation(long establishmentId) => new(
        EstablishmentId: establishmentId,
        RequestTypeCode: "new",
        ContractTypeCode: "bill-of-quantities",
        IndustrialCityCode: "riyadh-2",
        PermitTypeCode: "electricity",
        WorkDescription: "حفر لتمديد كابلات كهربائية",
        IssueDate: new DateOnly(2026, 11, 1),
        EndDate: new DateOnly(2026, 11, 30),
        ExcavationMethodCode: "open-trench",
        WorkLocation: "شارع 12",
        LengthM: 120.5m,
        WidthM: 1.2m,
        DepthM: 1.5m,
        TotalPathLengthM: 120.5m,
        ProjectManagerName: "أحمد علي",
        ProjectManagerPhone: "+966500000000",
        ProjectManagerEmail: "pm@example.com",
        SupervisingEntity: "مكتب الإشراف الهندسي");

    public static async Task<RequestDetail> CreateDraft(HttpClient client)
    {
        var res = await client.PostAsJsonAsync(RequestsUrl, new { serviceCode = "excavation-permit" });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<RequestDetail>(Json))!;
    }

    public static Task<HttpResponseMessage> SaveExcavation(HttpClient client, long id, object body)
        => client.PutAsJsonAsync($"{RequestsUrl}/{id}/excavation", body, Json);

    public static Task<HttpResponseMessage> Submit(HttpClient client, long id)
        => client.PostAsync($"{RequestsUrl}/{id}/submit", null);

    public static Task<HttpResponseMessage> Upload(
        HttpClient client, long id, string slot, byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        return client.PostAsync($"{RequestsUrl}/{id}/attachments/{slot}", form);
    }

    public static async Task UploadAllRequired(HttpClient client, long id)
    {
        foreach (var slot in ExcavationSlots)
        {
            var res = slot == "workLocationPhoto"
                ? await Upload(client, id, slot, Png, "site.png", "image/png")
                : await Upload(client, id, slot, Pdf, $"{slot}.pdf", "application/pdf");
            Assert.True(res.IsSuccessStatusCode, $"{slot}: {await res.Content.ReadAsStringAsync()}");
        }
    }

    public static async Task<RequestDetail> Read(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<RequestDetail>(Json))!;
    }

    /// <summary>Reads ValidationProblem "errors" (field → error codes).</summary>
    public static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage res)
    {
        Assert.Equal(400, (int)res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }

    public static async Task<string> ProblemTitle(HttpResponseMessage res)
        => (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;
}
