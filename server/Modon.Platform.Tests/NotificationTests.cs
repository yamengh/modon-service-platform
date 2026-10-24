using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modon.Platform.Data;
using Modon.Platform.Notifications;
using Modon.Platform.Requests;
using Modon.Platform.Requests.Building;
using static Modon.Platform.Tests.RequestTestKit;

namespace Modon.Platform.Tests;

/// <summary>In-app notifications: who receives them, no duplicates, privacy, read state, and the delivery channels.</summary>
[Collection("Modon API")]
public class NotificationTests(ModonAppFactory factory)
{
    private const string Url = "/api/modon/notifications";
    private readonly BuildingTestKit _kit = new(factory);

    private static async Task<NotificationPage> Mine(HttpClient client, string query = "")
        => (await client.GetFromJsonAsync<NotificationPage>(Url + query, Json))!;

    private static Task<HttpResponseMessage> Decide(HttpClient reviewer, long id, string action, object? body = null)
        => reviewer.PostAsJsonAsync($"/api/modon/review/requests/{id}/{action}", body ?? new { comment = "c" }, Json);

    private async Task<(HttpClient Client, long EstablishmentId, RequestDetail Request)> SubmittedExcavation()
    {
        var (email, establishmentId) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var client = await factory.LoginAs(email);
        var draft = await CreateDraft(client);
        await Read(await SaveExcavation(client, draft.Id, ValidExcavation(establishmentId)));
        await UploadAllRequired(client, draft.Id);
        return (client, establishmentId, await Read(await Submit(client, draft.Id)));
    }

    private async Task<List<ModonNotification>> RowsFor(long requestId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ModonDbContext>().Notifications.AsNoTracking()
            .Where(n => n.RequestId == requestId).OrderBy(n => n.Id).ToListAsync();
    }

    [Fact]
    public async Task ExcavationLifecycle_NotifiesTheApplicant_OncePerStep_WithTheReviewerComment()
    {
        var (applicant, _, request) = await SubmittedExcavation();
        var reviewer = await factory.LoginAs("reviewer@modon.local");

        Assert.Equal(HttpStatusCode.OK, (await Decide(reviewer, request.Id, "return", new { comment = "صحح موقع العمل" })).StatusCode);
        await Read(await Submit(applicant, request.Id));
        await Decide(reviewer, request.Id, "request-coordination-letters", new { comment = (string?)null });
        foreach (var slot in ServiceApplications.ExcavationPermit.CoordinationSlots)
            await Upload(applicant, request.Id, slot.Code, Pdf, "l.pdf", "application/pdf");
        await Read(await applicant.PostAsync($"{RequestsUrl}/{request.Id}/coordination-letters/submit", null));
        Assert.Equal(HttpStatusCode.OK, (await Decide(reviewer, request.Id, "approve")).StatusCode);

        var page = await Mine(applicant);
        var mine = page.Items.Where(n => n.RequestId == request.Id).Reverse().ToArray();
        Assert.Equal(
            [RequestAction.Submit, RequestAction.ReturnForCompletion, RequestAction.Resubmit, RequestAction.RequestCoordinationLetters,
             RequestAction.SubmitCoordinationLetters, RequestAction.Approve],
            mine.Select(n => n.Action));
        Assert.All(mine, n => Assert.Equal((NotificationKind.RequestUpdate, $"/requests/{request.Id}"), (n.Kind, n.Link)));
        Assert.Equal("صحح موقع العمل", mine[1].Comment);
        Assert.Equal($"Modon returned the request for completion — {request.RequestNumber}", mine[1].TitleEn);
        Assert.StartsWith("أعادت مدن الطلب لإكمال النواقص", mine[1].TitleAr);
        Assert.Equal(6, page.UnreadCount);

        // Exactly one row per history step; the reviewer is not notified about their own decisions.
        var rows = await RowsFor(request.Id);
        Assert.Equal(rows.Count, rows.Select(r => r.HistoryId).Distinct().Count());
        Assert.Empty((await Mine(reviewer)).Items.Where(n => n.RequestId == request.Id));
    }

    [Fact]
    public async Task Notifications_AreVisibleAndChangeableOnlyByTheirRecipient()
    {
        var (applicant, _, request) = await SubmittedExcavation();
        var (otherEmail, _) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var other = await factory.LoginAs(otherEmail);
        var id = (await Mine(applicant)).Items.Single(n => n.RequestId == request.Id).Id;

        Assert.DoesNotContain((await Mine(other)).Items, n => n.RequestId == request.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"{Url}/{id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync(Url)).StatusCode);
        Assert.False((await Mine(applicant)).Items.Single(n => n.Id == id).Read);

        Assert.Equal(HttpStatusCode.NoContent, (await applicant.PostAsync($"{Url}/{id}/read", null)).StatusCode);
        Assert.True((await Mine(applicant)).Items.Single(n => n.Id == id).Read);
        Assert.Empty((await Mine(applicant, "?unreadOnly=true")).Items);
        var count = await applicant.GetFromJsonAsync<JsonElement>($"{Url}/unread-count");
        Assert.Equal(0, count.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task RepeatedOrConflictingDecisions_ProduceOneNotification()
    {
        var (applicant, _, request) = await SubmittedExcavation();
        var first = await factory.LoginAs("reviewer@modon.local");
        var second = await factory.LoginAs("reviewer@modon.local");

        var results = await Task.WhenAll(Decide(first, request.Id, "approve"), Decide(second, request.Id, "reject", new { reason = "r" }),
            Decide(first, request.Id, "approve"));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        await Submit(applicant, request.Id); // refused: nothing to resubmit

        var decisions = (await RowsFor(request.Id)).Where(n => n.Action is RequestAction.Approve or RequestAction.Reject).ToList();
        Assert.Single(decisions);
        Assert.Equal(2, (await RowsFor(request.Id)).Count); // submit + the one decision
    }

    [Fact]
    public async Task BuildingLicence_NotifiesTheRightOffices_AndTheInvestor()
    {
        var (investor, request) = await _kit.Submitted();
        var chosen = await _kit.Consultant();
        var loser = await _kit.Consultant();
        var invitedContractor = await _kit.Contractor();

        // Open project: two quotations, the investor picks one.
        await Read(await BuildingTestKit.Choose(investor.Client, request.Id, "consultant", SelectionMode.OpenTender));
        var pick = (await BuildingTestKit.Project(await BuildingTestKit.Quote(chosen.Client, request.Id, chosen.EstablishmentId, 100m))).MyQuotations.Single();
        await BuildingTestKit.Project(await BuildingTestKit.Quote(loser.Client, request.Id, loser.EstablishmentId, 90m));
        await Read(await BuildingTestKit.AcceptQuotation(investor.Client, request.Id, "consultant", pick.Id));
        Assert.Contains((await Mine(chosen.Client)).Items, n => n.RequestId == request.Id && n.Kind == NotificationKind.QuotationSelected && n.Link == $"/projects/{request.Id}");
        Assert.Contains((await Mine(loser.Client)).Items, n => n.RequestId == request.Id && n.Kind == NotificationKind.QuotationNotSelected);

        // Contractor chosen directly: it is invited; when it accepts, the investor hears about it.
        await Read(await BuildingTestKit.Choose(investor.Client, request.Id, "contractor", SelectionMode.Direct, invitedContractor.EstablishmentId));
        Assert.Contains((await Mine(invitedContractor.Client)).Items, n => n.RequestId == request.Id && n.Kind == NotificationKind.OfficeInvited);
        await BuildingTestKit.Project(await BuildingTestKit.Respond(invitedContractor.Client, request.Id,
            new OfficeResponseBody(OfficeDecision.Accept, 5_000m, null)));

        var reviewer = await factory.LoginAs("reviewer@modon.local");
        Assert.Equal(HttpStatusCode.OK, (await Decide(reviewer, request.Id, "approve")).StatusCode);
        Assert.Contains((await Mine(chosen.Client)).Items, n => n.RequestId == request.Id && n.Kind == NotificationKind.ProjectApproved);
        Assert.Contains((await Mine(invitedContractor.Client)).Items, n => n.RequestId == request.Id && n.Kind == NotificationKind.ProjectApproved);
        Assert.DoesNotContain((await Mine(loser.Client)).Items, n => n.RequestId == request.Id && n.Kind == NotificationKind.ProjectApproved);

        await BuildingTestKit.Project(await BuildingTestKit.Report(chosen.Client, request.Id, DateTime.UtcNow.ToString("yyyy-MM"),
            BuildingTestKit.Progress(10, 0, 0, 0, 0, 0)));

        // The investor: submission confirmation, the contractor's acceptance, the approval and the report —
        // not their own stage choices.
        var investorActions = (await Mine(investor.Client)).Items.Where(n => n.RequestId == request.Id).Select(n => n.Action).Reverse();
        Assert.Equal([RequestAction.Submit, RequestAction.ContractorAccepted, RequestAction.Approve, RequestAction.SubmitProgressReport], investorActions);
    }

    [Fact]
    public async Task SavedNotifications_AreDelivered_ToFormCmsInbox_AndTheEmailStub()
    {
        var (email, establishmentId) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var client = await factory.LoginAs(email);
        var draft = await CreateDraft(client);
        await Read(await SaveExcavation(client, draft.Id, ValidExcavation(establishmentId)));
        await UploadAllRequired(client, draft.Id);
        await Read(await Submit(client, draft.Id));

        var row = (await RowsFor(draft.Id)).Single();
        Assert.NotNull(row.DispatchedAt);
        Assert.Equal(1, row.DispatchAttempts);
        var log = factory.Services.GetRequiredService<DevDeliveryLog>();
        Assert.Contains(log.Entries, e => e.Channel == "email" && e.To == email && e.Text.Contains(draft.RequestNumber));

        // FormCMS's own notification list shows it to the recipient only.
        var formCms = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Contains(formCms.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("url").GetString() == $"/requests/{draft.Id}" && i.GetProperty("message").GetString()!.Contains(draft.RequestNumber));
        var (otherEmail, _) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var other = await (await factory.LoginAs(otherEmail)).GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.DoesNotContain(other.GetProperty("items").EnumerateArray(), i => i.GetProperty("url").GetString() == $"/requests/{draft.Id}");
    }

    [Fact]
    public async Task Dispatcher_ClaimsEachNotificationOnce()
    {
        var (_, _, request) = await SubmittedExcavation();
        var id = (await RowsFor(request.Id)).Single().Id;

        using var scope = factory.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
        Assert.Equal(0, await dispatcher.DispatchAsync([id], CancellationToken.None)); // already delivered
        Assert.Equal(1, (await RowsFor(request.Id)).Single().DispatchAttempts);
    }
}
