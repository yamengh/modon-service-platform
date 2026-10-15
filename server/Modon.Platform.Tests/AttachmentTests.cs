using System.Net;
using System.Net.Http.Json;
using Modon.Platform.Requests;
using static Modon.Platform.Tests.RequestTestKit;

namespace Modon.Platform.Tests;

[Collection("Modon API")]
public class AttachmentTests(ModonAppFactory factory)
{
    private async Task<(HttpClient Client, RequestDetail Draft)> NewDraft()
    {
        var (email, _) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var client = await factory.LoginAs(email);
        return (client, await CreateDraft(client));
    }

    private static async Task AssertFileError(HttpResponseMessage res, string slot, string code)
        => Assert.Equal([code], (await Errors(res))[$"attachments.{slot}"]);

    [Fact]
    public async Task ValidPdf_IsStored_AndOnlyDownloadableByTheOwner()
    {
        var (client, draft) = await NewDraft();

        var upload = await Upload(client, draft.Id, "workRoutePlan", Pdf, "مخطط المسار.pdf", "application/pdf");
        var info = await upload.Content.ReadFromJsonAsync<AttachmentInfo>(Json);
        var download = await client.GetAsync($"{RequestsUrl}/{draft.Id}/attachments/workRoutePlan");

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(("مخطط المسار.pdf", "application/pdf", Pdf.Length), (info!.FileName, info.ContentType, info.SizeBytes));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());

        var (intruder, _) = await NewDraft();
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.GetAsync($"{RequestsUrl}/{draft.Id}/attachments/workRoutePlan")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.NewClient().GetAsync($"{RequestsUrl}/{draft.Id}/attachments/workRoutePlan")).StatusCode);
    }

    [Fact]
    public async Task StoredFiles_AreNotReachableThroughPublicUrls()
    {
        var (client, draft) = await NewDraft();
        (await Upload(client, draft.Id, "workRoutePlan", Pdf, "route.pdf", "application/pdf")).EnsureSuccessStatusCode();

        var stored = Directory.GetFiles(factory.DocumentsRoot, "*", SearchOption.AllDirectories)
            .Single(f => f.Contains($"{Path.DirectorySeparatorChar}{draft.Id}{Path.DirectorySeparatorChar}"));
        var relative = Path.GetRelativePath(factory.DocumentsRoot, stored).Replace('\\', '/');

        Assert.DoesNotContain("route", Path.GetFileName(stored)); // the user's file name is not used on disk
        foreach (var url in new[] { $"/files/{relative}", $"/{relative}" })
            Assert.Equal(HttpStatusCode.NotFound, (await factory.NewClient().GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task FileLargerThan20480Kb_IsRejected()
    {
        var (client, draft) = await NewDraft();
        var tooBig = new byte[ServiceApplications.MaxFileSizeKb * 1024 + 1];
        Pdf.CopyTo(tooBig, 0);
        var exactlyMax = new byte[ServiceApplications.MaxFileSizeKb * 1024];
        Pdf.CopyTo(exactlyMax, 0);

        await AssertFileError(await Upload(client, draft.Id, "projectSchedule", tooBig, "big.pdf", "application/pdf"),
            "projectSchedule", ErrorCodes.FileTooLarge);
        Assert.Equal(HttpStatusCode.OK,
            (await Upload(client, draft.Id, "projectSchedule", exactlyMax, "max.pdf", "application/pdf")).StatusCode);
    }

    [Fact]
    public async Task DisallowedExtensions_AreRejectedPerSlot()
    {
        var (client, draft) = await NewDraft();

        // The work location photo accepts images only.
        await AssertFileError(await Upload(client, draft.Id, "workLocationPhoto", Pdf, "site.pdf", "application/pdf"),
            "workLocationPhoto", ErrorCodes.FileTypeNotAllowed);
        await AssertFileError(await Upload(client, draft.Id, "trafficSafetyPlan", "GIF89a"u8.ToArray(), "plan.gif", "image/gif"),
            "trafficSafetyPlan", ErrorCodes.FileTypeNotAllowed);
        await AssertFileError(await Upload(client, draft.Id, "trafficSafetyPlan", Pdf, "plan.exe", "application/octet-stream"),
            "trafficSafetyPlan", ErrorCodes.FileTypeNotAllowed);

        Assert.Equal(HttpStatusCode.OK,
            (await Upload(client, draft.Id, "workLocationPhoto", Jpeg, "site.JPG", "image/jpeg")).StatusCode);
    }

    [Fact]
    public async Task ContentThatDoesNotMatchTheFileType_IsRejected()
    {
        var (client, draft) = await NewDraft();

        // .png name, but the bytes are a PDF
        await AssertFileError(await Upload(client, draft.Id, "workLocationPhoto", Pdf, "site.png", "image/png"),
            "workLocationPhoto", ErrorCodes.FileContentMismatch);
        // .pdf with a declared image MIME type
        await AssertFileError(await Upload(client, draft.Id, "ownerEntityLetter", Pdf, "letter.pdf", "image/png"),
            "ownerEntityLetter", ErrorCodes.FileTypeNotAllowed);
        // empty file
        await AssertFileError(await Upload(client, draft.Id, "ownerEntityLetter", [], "letter.pdf", "application/pdf"),
            "ownerEntityLetter", ErrorCodes.FileEmpty);
    }

    [Fact]
    public async Task UnknownSlot_IsRejected()
    {
        var (client, draft) = await NewDraft();

        var res = await Upload(client, draft.Id, "coordinationLetterSec", Pdf, "x.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal(ProblemCodes.UnknownSlot, await ProblemTitle(res));
    }

    [Fact]
    public async Task Uploading_Again_ReplacesTheFile_AndDeleteRemovesIt()
    {
        var (client, draft) = await NewDraft();
        (await Upload(client, draft.Id, "workLocationPhoto", Png, "first.png", "image/png")).EnsureSuccessStatusCode();
        (await Upload(client, draft.Id, "workLocationPhoto", Jpeg, "second.jpg", "image/jpeg")).EnsureSuccessStatusCode();

        var afterReplace = await Read(await client.GetAsync($"{RequestsUrl}/{draft.Id}"));
        var file = Assert.Single(afterReplace.Attachments);
        Assert.Equal("second.jpg", file.FileName);
        Assert.Equal(Jpeg, await client.GetByteArrayAsync($"{RequestsUrl}/{draft.Id}/attachments/workLocationPhoto"));
        Assert.Single(Directory.GetFiles(Path.Combine(factory.DocumentsRoot, "requests", draft.Id.ToString()),
            "*", SearchOption.AllDirectories));

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"{RequestsUrl}/{draft.Id}/attachments/workLocationPhoto")).StatusCode);
        var afterDelete = await Read(await client.GetAsync($"{RequestsUrl}/{draft.Id}"));
        Assert.Empty(afterDelete.Attachments);
        Assert.Empty(Directory.GetFiles(Path.Combine(factory.DocumentsRoot, "requests", draft.Id.ToString()),
            "*", SearchOption.AllDirectories));
    }
}
