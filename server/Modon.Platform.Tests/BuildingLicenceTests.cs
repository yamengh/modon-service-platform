using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modon.Platform.Data;
using Modon.Platform.Requests;
using Modon.Platform.Requests.Building;
using Modon.Platform.Review;
using static Modon.Platform.Tests.BuildingTestKit;
using static Modon.Platform.Tests.RequestTestKit;

namespace Modon.Platform.Tests;

/// <summary>Building licence (Milestones 6–8) with the in-process workflow engine.</summary>
[Collection("Modon API")]
public class BuildingLicenceTests(ModonAppFactory factory)
{
    private readonly BuildingTestKit _kit = new(factory);

    private async Task<T> Body<T>(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<int> HistoryRows(long id, RequestAction action)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ModonDbContext>().RequestStatusHistory
            .CountAsync(h => h.RequestId == id && h.Action == action);
    }

    // --- application form ------------------------------------------------------------------------

    [Fact]
    public async Task Draft_IsCreatedWithABuildingNumber_AndCanBeSavedReopenedAndEdited()
    {
        var investor = await _kit.Investor();

        var draft = await CreateBuildingDraft(investor.Client);
        Assert.Matches(@"^BLR-\d{6}-\d{2}-\d{2}-\d{2}$", draft.RequestNumber); // like the manual's BLR-000004-22-01-26
        Assert.Equal((RequestStatus.Draft, true), (draft.Status, draft.CanEdit));
        Assert.NotNull(draft.Building);

        var form = ValidForm(investor.EstablishmentId) with
        {
            Contacts = [new("Sameh", "sameh@example.com", "01201001203"), new("Investor", "investor@persona.com", "0555555555")],
        };
        await Read(await Save(investor.Client, draft.Id, form));
        var reopened = await Get(investor.Client, draft.Id);
        Assert.Equal(form.ContractNumber, reopened.Building!.Form.ContractNumber);
        Assert.Equal((10_000_000m, 11, true), (reopened.Building.Form.ConstructionBudgetSar!.Value, reopened.Building.Form.FloorCount!.Value, reopened.Building.Form.TermsAccepted));
        Assert.Equal(["Sameh", "Investor"], reopened.Building.Form.Contacts!.Select(c => c.Name));

        // Editing replaces the contact list and keeps the request number.
        await Read(await Save(investor.Client, draft.Id, form with { Contacts = [new("Only", "only@example.com", "+966500000000")], FloorCount = 3 }));
        var edited = await Get(investor.Client, draft.Id);
        Assert.Equal((draft.RequestNumber, 3), (edited.RequestNumber, edited.Building!.Form.FloorCount!.Value));
        Assert.Equal(["Only"], edited.Building.Form.Contacts!.Select(c => c.Name));
        Assert.Equal([RequestAction.Create], edited.History.Select(h => h.Action));
    }

    [Fact]
    public async Task Draft_ChecksFormatsOnSave_AndEverythingRequiredOnSubmit()
    {
        var investor = await _kit.Investor();
        var draft = await CreateBuildingDraft(investor.Client);

        var bad = await Errors(await Save(investor.Client, draft.Id, new BuildingLicenceDto(
            ContractNumber: "25 000 168!", LicenceTypeCode: "skyscraper", ConstructionBudgetSar: -5, FloorCount: 0,
            Contacts: [new("Sameh", "not-an-email", "12")])));
        Assert.Equal(ErrorCodes.InvalidFormat, bad["contractNumber"][0]);
        Assert.Equal(ErrorCodes.UnknownOption, bad["licenceTypeCode"][0]);
        Assert.Equal(ErrorCodes.MustBePositive, bad["constructionBudgetSar"][0]);
        Assert.Equal(ErrorCodes.MustBePositive, bad["floorCount"][0]);
        Assert.Equal(ErrorCodes.InvalidEmail, bad["contacts[0].email"][0]);
        Assert.Equal(ErrorCodes.InvalidPhone, bad["contacts[0].mobile"][0]);

        var other = await _kit.Investor();
        var notMine = await Errors(await Save(investor.Client, draft.Id, new BuildingLicenceDto(EstablishmentId: other.EstablishmentId)));
        Assert.Equal(ErrorCodes.NotYourEstablishment, notMine["establishmentId"][0]);

        // An empty draft saves; submitting it lists every required field, the terms and both plans.
        await Read(await Save(investor.Client, draft.Id, new BuildingLicenceDto()));
        var missing = await Errors(await Submit(investor.Client, draft.Id));
        foreach (var field in new[] { "establishmentId", "contractNumber", "licenceTypeCode", "constructionBudgetSar", "floorCount",
                     "termsAccepted", "attachments.approvedGeneralPlans", "attachments.approvedSafetyPlans" })
            Assert.Equal(ErrorCodes.Required, missing[field][0]);
        Assert.False(missing.ContainsKey("scopeOfWork")); // not marked required in the manual
        Assert.False(missing.ContainsKey("attachments.generalAttachment")); // optional

        await Read(await Save(investor.Client, draft.Id, ValidForm(investor.EstablishmentId) with { TermsAccepted = false }));
        await UploadPlans(investor.Client, draft.Id);
        var terms = await Errors(await Submit(investor.Client, draft.Id));
        Assert.Equal(["termsAccepted"], terms.Keys);
        Assert.Equal(RequestStatus.Draft, (await Get(investor.Client, draft.Id)).Status);
    }

    [Fact]
    public async Task Uploads_AcceptTheManualsFormats_AndRejectOthers()
    {
        var investor = await _kit.Investor();
        var draft = await CreateBuildingDraft(investor.Client);
        byte[] docx = [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4];

        Assert.True((await Upload(investor.Client, draft.Id, "generalAttachment", docx, "spec.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document")).IsSuccessStatusCode);
        var fake = await Errors(await Upload(investor.Client, draft.Id, "generalAttachment", Pdf, "spec.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));
        Assert.Equal(ErrorCodes.FileContentMismatch, fake["attachments.generalAttachment"][0]);
        var jpegPlan = await Errors(await Upload(investor.Client, draft.Id, "approvedGeneralPlans", Jpeg, "plan.jpg", "image/jpeg"));
        Assert.Equal(ErrorCodes.FileTypeNotAllowed, jpegPlan["attachments.approvedGeneralPlans"][0]); // plans: PDF / PNG
        var excavationSlot = await Upload(investor.Client, draft.Id, "workRoutePlan", Pdf, "x.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.NotFound, excavationSlot.StatusCode);
    }

    [Fact]
    public async Task Submit_StartsConsultantSelection_LocksTheForm_AndRecordsHistory()
    {
        var (investor, submitted) = await _kit.Submitted();

        Assert.Equal((RequestStatus.ConsultantSelection, false), (submitted.Status, submitted.CanEdit));
        Assert.NotNull(submitted.SubmittedAt);
        Assert.Equal(ApplicantTaskKind.ChooseConsultant, submitted.CurrentTask!.Kind);
        Assert.Equal([RequestAction.OpenConsultantTender, RequestAction.SelectConsultant], submitted.Building!.InvestorActions);
        Assert.Equal([RequestAction.Create, RequestAction.Submit], submitted.History.Select(h => h.Action));

        await AssertProblem(await Save(investor.Client, submitted.Id, ValidForm(investor.EstablishmentId)), HttpStatusCode.Conflict, ProblemCodes.RequestNotEditable);
        await AssertProblem(await Submit(investor.Client, submitted.Id), HttpStatusCode.Conflict, ProblemCodes.RequestNotEditable);
        await AssertProblem(await Upload(investor.Client, submitted.Id, "approvedGeneralPlans", Pdf, "p.pdf", "application/pdf"),
            HttpStatusCode.Conflict, ProblemCodes.RequestNotEditable);
        // Excavation-only endpoints refuse building requests.
        await AssertProblem(await SaveExcavation(investor.Client, submitted.Id, new ExcavationDto()), HttpStatusCode.BadRequest, ProblemCodes.WrongService);
        Assert.Contains((await Body<TaskItem[]>(await investor.Client.GetAsync("/api/modon/tasks"))),
            t => t.RequestId == submitted.Id && t.Kind == ApplicantTaskKind.ChooseConsultant);
    }

    // --- consultant: open project (tender) -------------------------------------------------------

    [Fact]
    public async Task OpenProject_OfficesQuote_InvestorPicksOne_AndMovesToContractorSelection()
    {
        var (investor, request) = await _kit.Submitted();
        var first = await _kit.Consultant();
        var second = await _kit.Consultant();
        var contractor = await _kit.Contractor();

        var opened = await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.OpenTender));
        Assert.Equal(RequestStatus.ConsultantTender, opened.Status);
        var round = opened.Building!.Tenders.Single();
        Assert.Matches(@"^Bid-\d{5}-\d{2}-\d{2}-\d{2}$", round.Number); // like the manual's Bid-00080-30-12-25
        Assert.Null(opened.CurrentTask); // nothing to decide before the first quotation

        // Consulting offices see it among the open projects (without contacts or plans); contractors don't.
        var listed = await Body<PartnerProjectSummary[]>(await first.Client.GetAsync(ProjectsUrl));
        Assert.Contains(listed, p => p.RequestId == request.Id && p.Relation == PartnerRelation.OpenTender && p.ProjectNumber == round.Number);
        var view = await GetProject(first.Client, request.Id);
        Assert.Equal((true, 0), (view.CanQuote, view.Attachments.Length));
        Assert.DoesNotContain(await Body<PartnerProjectSummary[]>(await contractor.Client.GetAsync(ProjectsUrl)), p => p.RequestId == request.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await contractor.Client.GetAsync($"{ProjectsUrl}/{request.Id}")).StatusCode);

        var q1 = (await Project(await Quote(first.Client, request.Id, first.EstablishmentId, 200_000m))).MyQuotations.Single();
        await Project(await Quote(second.Client, request.Id, second.EstablishmentId, 180_000m, comment: null));
        await AssertProblem(await Quote(first.Client, request.Id, first.EstablishmentId, 150_000m), HttpStatusCode.Conflict, ProblemCodes.QuotationAlreadySubmitted);
        Assert.Equal(HttpStatusCode.NotFound, (await Quote(contractor.Client, request.Id, contractor.EstablishmentId, 1m)).StatusCode);
        // An office the user does not belong to, or an amount that is not positive.
        await AssertProblem(await Quote(first.Client, request.Id, second.EstablishmentId, 1m), HttpStatusCode.Forbidden, ProblemCodes.NotYourEstablishment);
        Assert.Equal(ErrorCodes.MustBePositive, (await Errors(await Quote(second.Client, request.Id, second.EstablishmentId, 0m)))["amountSar"][0]);

        // The investor sees both quotations and has a task to choose.
        var withQuotes = await Get(investor.Client, request.Id);
        Assert.Equal([200_000m, 180_000m], withQuotes.Building!.Tenders.Single().Quotations.Select(q => q.AmountSar!.Value));
        Assert.Equal(ApplicantTaskKind.ReviewConsultantQuotations, withQuotes.CurrentTask!.Kind);
        // Offices never see each other's quotations.
        Assert.Equal([200_000m], (await GetProject(first.Client, request.Id)).MyQuotations.Select(q => q.AmountSar!.Value));

        var chosen = await Read(await AcceptQuotation(investor.Client, request.Id, "consultant", q1.Id));
        Assert.Equal(RequestStatus.ContractorSelection, chosen.Status);
        Assert.Equal(first.EstablishmentId, chosen.Building!.SelectedConsultant!.Id);
        Assert.Equal(q1.Id, chosen.Building.ConsultantQuotation!.Id);
        var closed = chosen.Building.Tenders.Single();
        Assert.Equal(TenderState.Awarded, closed.State);
        Assert.Equal([QuotationState.Selected, QuotationState.NotSelected], closed.Quotations.Select(q => q.State));

        // The project is closed: no more quotations, no second choice.
        await AssertProblem(await AcceptQuotation(investor.Client, request.Id, "consultant", q1.Id), HttpStatusCode.Conflict, ProblemCodes.InvalidTransition);
        Assert.Equal(PartnerRelation.Awarded, (await GetProject(first.Client, request.Id)).Relation);
        Assert.Equal(HttpStatusCode.NotFound, (await second.Client.GetAsync($"{ProjectsUrl}/{request.Id}")).StatusCode);
        Assert.Equal(1, await HistoryRows(request.Id, RequestAction.AcceptConsultantQuotation));
    }

    [Fact]
    public async Task OpenProject_RejectAll_ReturnsToConsultantSelection_AndANewRoundCanStart()
    {
        var (investor, request) = await _kit.Submitted();
        var consultant = await _kit.Consultant();
        await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.OpenTender));
        await Project(await Quote(consultant.Client, request.Id, consultant.EstablishmentId, 300_000m));

        var back = await Read(await RejectQuotations(investor.Client, request.Id, "consultant", "الأسعار مرتفعة"));

        Assert.Equal(RequestStatus.ConsultantSelection, back.Status);
        var round = back.Building!.Tenders.Single();
        Assert.Equal((TenderState.Closed, QuotationState.NotSelected), (round.State, round.Quotations.Single().State));
        Assert.Equal((ApplicantTaskKind.ChooseConsultant, "الأسعار مرتفعة"), (back.CurrentTask!.Kind, back.CurrentTask.Instructions));
        Assert.Equal(HttpStatusCode.NotFound, (await Quote(consultant.Client, request.Id, consultant.EstablishmentId, 1m)).StatusCode);

        var again = await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.OpenTender));
        Assert.Equal(2, again.Building!.Tenders.Length);
        Assert.NotEqual(again.Building.Tenders[0].Number, again.Building.Tenders[1].Number);
        await Project(await Quote(consultant.Client, request.Id, consultant.EstablishmentId, 250_000m)); // a new round, a new quotation
    }

    // --- consultant: specific office ---------------------------------------------------------------

    [Fact]
    public async Task SpecificOffice_Accepts_WithAmountPhasesCommitmentsAndAttachment()
    {
        var (investor, request) = await _kit.Submitted();
        var consultant = await _kit.Consultant();
        var outsider = await _kit.Consultant();

        var offices = await Body<OfficeInfo[]>(await investor.Client.GetAsync("/api/modon/building/offices?stage=consultant"));
        Assert.Contains(offices, o => o.Id == consultant.EstablishmentId);
        Assert.DoesNotContain(offices, o => o.Id == investor.EstablishmentId);
        Assert.Equal(ErrorCodes.NotAnOffice, (await Errors(await Choose(investor.Client, request.Id, "consultant", SelectionMode.Direct, investor.EstablishmentId)))["establishmentId"][0]);

        var waiting = await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.Direct, consultant.EstablishmentId));
        Assert.Equal(RequestStatus.AwaitingConsultantDecision, waiting.Status);
        Assert.Contains(await Body<TaskItem[]>(await consultant.Client.GetAsync("/api/modon/tasks")),
            t => t.RequestId == request.Id && t.Kind == ApplicantTaskKind.RespondAsConsultant);
        var invited = await GetProject(consultant.Client, request.Id);
        Assert.Equal((PartnerRelation.Invited, true), (invited.Relation, invited.CanRespond));
        Assert.Equal(["approvedGeneralPlans", "approvedSafetyPlans"], invited.Attachments.Select(a => a.SlotCode)); // the invited office sees the plans
        Assert.True((await consultant.Client.GetAsync($"{ProjectsUrl}/{request.Id}/attachments/approvedGeneralPlans")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync($"{ProjectsUrl}/{request.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Respond(outsider.Client, request.Id, ConsultantAccepts())).StatusCode);

        // Accepting needs the commitments and all six phases, each 0–100 %.
        var invalid = await Errors(await Respond(consultant.Client, request.Id, ConsultantAccepts() with
        {
            CommitmentsAccepted = false,
            Phases = [.. FullPlan().Take(5).Select((p, i) => i == 0 ? p with { SharePercent = 120 } : i == 1 ? p with { DurationDays = 0 } : p)],
        }));
        Assert.Equal(ErrorCodes.Required, invalid["commitmentsAccepted"][0]);
        Assert.Equal(ErrorCodes.OutOfRange, invalid["phases[0].sharePercent"][0]);
        Assert.Equal(ErrorCodes.MustBePositive, invalid["phases[1].durationDays"][0]);
        Assert.Equal(ErrorCodes.Required, invalid["phases"][0]); // one phase missing
        var noAmount = await Errors(await Respond(consultant.Client, request.Id, ConsultantAccepts() with { AmountSar = null }));
        Assert.Equal(ErrorCodes.Required, noAmount["amountSar"][0]);

        byte[] xlsx = [0x50, 0x4B, 0x03, 0x04, 9, 9, 9, 9];
        var accepted = await Project(await RespondWithFile(consultant.Client, request.Id, ConsultantAccepts(), xlsx, "plan.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        Assert.Equal(PartnerRelation.Awarded, accepted.Relation);
        Assert.Equal(116m, accepted.ConsultantPlan!.Phases.Sum(p => p.SharePercent!.Value)); // no "must total 100" rule

        var after = await Get(investor.Client, request.Id);
        Assert.Equal((RequestStatus.ContractorSelection, consultant.EstablishmentId), (after.Status, after.Building!.SelectedConsultant!.Id));
        var answer = after.Building.ConsultantQuotation!;
        Assert.Equal((4_000_000m, true, "plan.xlsx", 6), (answer.AmountSar!.Value, answer.CommitmentsAccepted, answer.Attachment!.FileName, answer.Phases.Length));
        Assert.True((await investor.Client.GetAsync($"{RequestsUrl}/{request.Id}/building/quotations/{answer.Id}/attachment")).IsSuccessStatusCode);
        await AssertProblem(await Respond(consultant.Client, request.Id, ConsultantAccepts()), HttpStatusCode.Conflict, ProblemCodes.InvalidTransition);
    }

    [Fact]
    public async Task SpecificOffice_RejectsWithAComment_AndTheInvestorChoosesAgain()
    {
        var (investor, request) = await _kit.Submitted();
        var consultant = await _kit.Consultant();
        await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.Direct, consultant.EstablishmentId));

        var noComment = await Errors(await Respond(consultant.Client, request.Id, new OfficeResponseBody(OfficeDecision.Reject, null, "  ")));
        Assert.Equal(ErrorCodes.Required, noComment["comment"][0]);

        Assert.Equal(HttpStatusCode.NoContent, (await Respond(consultant.Client, request.Id, new OfficeResponseBody(OfficeDecision.Reject, null, "لا نستطيع الالتزام بالمدة"))).StatusCode);

        var back = await Get(investor.Client, request.Id);
        Assert.Equal(RequestStatus.ConsultantSelection, back.Status);
        Assert.Equal((ApplicantTaskKind.ChooseConsultant, "لا نستطيع الالتزام بالمدة"), (back.CurrentTask!.Kind, back.CurrentTask.Instructions));
        Assert.Equal((TenderState.Closed, QuotationState.Declined), (back.Building!.Tenders.Single().State, back.Building.Tenders.Single().Quotations.Single().State));
        Assert.Null(back.Building.SelectedConsultant);
        Assert.Equal(HttpStatusCode.NotFound, (await consultant.Client.GetAsync($"{ProjectsUrl}/{request.Id}")).StatusCode);
        Assert.DoesNotContain(await Body<TaskItem[]>(await consultant.Client.GetAsync("/api/modon/tasks")), t => t.RequestId == request.Id);
    }

    // --- contractor stage ------------------------------------------------------------------------

    [Fact]
    public async Task ContractorStage_OpenProjectAndSpecificContractor_EndWithModonReview()
    {
        var (investor, request) = await _kit.Submitted();
        // Contractors only after the consultant stage.
        await AssertProblem(await Choose(investor.Client, request.Id, "contractor", SelectionMode.OpenTender),
            HttpStatusCode.Conflict, ProblemCodes.InvalidTransition);
        await _kit.ConsultantChosenByTender(investor, request.Id);

        // Open project for contractors; a consulting office cannot quote in it.
        var contractorA = await _kit.Contractor();
        var contractorB = await _kit.Contractor();
        var consultant = await _kit.Consultant();
        Assert.Equal(RequestStatus.ContractorTender, (await Read(await Choose(investor.Client, request.Id, "contractor", SelectionMode.OpenTender))).Status);
        await Project(await Quote(contractorA.Client, request.Id, contractorA.EstablishmentId, 20_000_000m, "شامل المواد"));
        Assert.Equal(HttpStatusCode.NotFound, (await Quote(consultant.Client, request.Id, consultant.EstablishmentId, 1m)).StatusCode);

        // All rejected → contractor selection again; then a specific contractor rejects, another accepts.
        Assert.Equal(RequestStatus.ContractorSelection, (await Read(await RejectQuotations(investor.Client, request.Id, "contractor"))).Status);
        await Read(await Choose(investor.Client, request.Id, "contractor", SelectionMode.Direct, contractorB.EstablishmentId));
        Assert.Contains(await Body<TaskItem[]>(await contractorB.Client.GetAsync("/api/modon/tasks")),
            t => t.RequestId == request.Id && t.Kind == ApplicantTaskKind.RespondAsContractor);
        Assert.Equal(HttpStatusCode.NoContent, (await Respond(contractorB.Client, request.Id, new OfficeResponseBody(OfficeDecision.Reject, null, "تم الرفض"))).StatusCode);
        Assert.Equal(RequestStatus.ContractorSelection, (await Get(investor.Client, request.Id)).Status);

        await Read(await Choose(investor.Client, request.Id, "contractor", SelectionMode.Direct, contractorA.EstablishmentId));
        var noAmount = await Errors(await Respond(contractorA.Client, request.Id, new OfficeResponseBody(OfficeDecision.Accept, null, null)));
        Assert.Equal(ErrorCodes.Required, noAmount["amountSar"][0]);
        await Project(await Respond(contractorA.Client, request.Id, new OfficeResponseBody(OfficeDecision.Accept, 2_000_000m, null)));

        var underReview = await Get(investor.Client, request.Id);
        Assert.Equal(RequestStatus.UnderModonReview, underReview.Status);
        Assert.Equal((contractorA.EstablishmentId, 2_000_000m),
            (underReview.Building!.SelectedContractor!.Id, underReview.Building.ContractorQuotation!.AmountSar!.Value));
        Assert.Empty(underReview.Building.InvestorActions);
        Assert.Equal(
            [RequestAction.Create, RequestAction.Submit, RequestAction.OpenConsultantTender, RequestAction.AcceptConsultantQuotation,
             RequestAction.OpenContractorTender, RequestAction.RejectContractorQuotations, RequestAction.SelectContractor,
             RequestAction.ContractorDeclined, RequestAction.SelectContractor, RequestAction.ContractorAccepted],
            underReview.History.Select(h => h.Action));
    }

    // --- Modon review -------------------------------------------------------------------------------

    [Fact]
    public async Task Reviewer_SeesEverything_AndCanOnlyApproveOrReject()
    {
        var (investor, consultant, contractor, request) = await _kit.UnderReview();
        var reviewer = await _kit.Reviewer();

        var detail = await Body<ReviewRequestDetail>(await reviewer.GetAsync($"/api/modon/review/requests/{request.Id}"));
        Assert.Equal(RequestStatus.UnderModonReview, detail.Status);
        Assert.Equal([RequestAction.Approve, RequestAction.Reject], detail.AvailableDecisions);
        Assert.Equal("Sameh", detail.Building!.Form.Contacts!.Single().Name);
        Assert.Equal((consultant.EstablishmentId, contractor.EstablishmentId), (detail.Building.SelectedConsultant!.Id, detail.Building.SelectedContractor!.Id));
        Assert.Equal(2, detail.Building.Tenders.Length);
        Assert.Empty(detail.Building.InvestorActions);
        Assert.Contains((await Body<ReviewQueuePage>(await reviewer.GetAsync("/api/modon/review/requests?status=underModonReview&pageSize=100"))).Items,
            i => i.Id == request.Id);

        // Excavation-only decisions are refused; the reason is required to reject.
        await AssertProblem(await Decide(reviewer, request.Id, "return"), HttpStatusCode.Conflict, ProblemCodes.InvalidTransition);
        await AssertProblem(await Decide(reviewer, request.Id, "request-coordination-letters"), HttpStatusCode.Conflict, ProblemCodes.InvalidTransition);
        Assert.Equal(ErrorCodes.Required, (await Errors(await Decide(reviewer, request.Id, "reject", new { reason = "" })))["reason"][0]);
        // Offices and investors cannot decide.
        Assert.Equal(HttpStatusCode.Forbidden, (await Decide(consultant.Client, request.Id, "approve")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Decide(investor.Client, request.Id, "approve")).StatusCode);

        var approved = await Body<ReviewRequestDetail>(await Decide(reviewer, request.Id, "approve", new { comment = "الرخص المرفقة مكتملة" }));
        Assert.Equal(RequestStatus.Approved, approved.Status);
        Assert.Equal(("الرخص المرفقة مكتملة", RequestAction.Approve), (approved.History[^1].Comment, approved.History[^1].Action));
        await AssertProblem(await Decide(reviewer, request.Id, "reject", new { reason = "late" }), HttpStatusCode.Conflict, ProblemCodes.InvalidTransition);
        Assert.Equal(RequestStatus.Approved, (await Get(investor.Client, request.Id)).Status);
    }

    [Fact]
    public async Task Reviewer_RejectsAnotherRequest_Finally()
    {
        var (investor, _, _, request) = await _kit.UnderReview();
        var reviewer = await _kit.Reviewer();

        var rejected = await Body<ReviewRequestDetail>(await Decide(reviewer, request.Id, "reject", new { reason = "المخططات غير معتمدة" }));

        Assert.Equal(RequestStatus.Rejected, rejected.Status);
        Assert.Equal("المخططات غير معتمدة", (await Get(investor.Client, request.Id)).History[^1].Comment);
        await AssertProblem(await Decide(reviewer, request.Id, "approve"), HttpStatusCode.Conflict, ProblemCodes.InvalidTransition);
    }

    [Fact]
    public async Task ConflictingDecisions_AtTheSameMoment_RecordOneOutcome()
    {
        var (_, _, _, request) = await _kit.UnderReview();
        var first = await _kit.Reviewer();
        var second = await _kit.Reviewer();

        var results = await Task.WhenAll(Decide(first, request.Id, "approve"), Decide(second, request.Id, "reject", new { reason = "r" }));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Select(r => r.StatusCode).Order());
        Assert.Equal(1, await HistoryRows(request.Id, RequestAction.Approve) + await HistoryRows(request.Id, RequestAction.Reject));
    }

    // --- ownership and organisation checks ----------------------------------------------------------

    [Fact]
    public async Task OtherUsers_CannotSeeOrActOnSomeoneElsesRequest()
    {
        var (investor, request) = await _kit.Submitted();
        var otherInvestor = await _kit.Investor();
        var consultant = await _kit.Consultant();

        Assert.Equal(HttpStatusCode.NotFound, (await otherInvestor.Client.GetAsync($"{RequestsUrl}/{request.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Choose(otherInvestor.Client, request.Id, "consultant", SelectionMode.OpenTender)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Choose(consultant.Client, request.Id, "consultant", SelectionMode.OpenTender)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await consultant.Client.GetAsync($"{RequestsUrl}/{request.Id}")).StatusCode);
        // Before an open project exists, offices cannot see it at all.
        Assert.Equal(HttpStatusCode.NotFound, (await consultant.Client.GetAsync($"{ProjectsUrl}/{request.Id}")).StatusCode);

        // A consultant user who is not a member of a registered consulting office has no projects.
        var (plainEmail, plainEstablishment) = await factory.CreateUserAsync([ModonRoles.Consultant]);
        var plain = await factory.LoginAs(plainEmail);
        await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.OpenTender));
        Assert.Empty(await Body<PartnerProjectSummary[]>(await plain.GetAsync(ProjectsUrl)));
        Assert.Equal(HttpStatusCode.NotFound, (await Quote(plain, request.Id, plainEstablishment, 1m)).StatusCode);

        // The investor cannot quote on their own project, even with a consulting office of their own.
        var (bothEmail, bothOffice) = await factory.CreateUserAsync([ModonRoles.Investor, ModonRoles.Consultant],
            Seeding.DevUserSeeder.OfficeKind.ConsultingOffice);
        var both = await factory.LoginAs(bothEmail);
        var own = await CreateBuildingDraft(both);
        await Read(await Save(both, own.Id, ValidForm(bothOffice)));
        await UploadPlans(both, own.Id);
        await Read(await Submit(both, own.Id));
        await Read(await Choose(both, own.Id, "consultant", SelectionMode.OpenTender));
        Assert.Equal(HttpStatusCode.NotFound, (await Quote(both, own.Id, bothOffice, 1m)).StatusCode);
    }

    [Fact]
    public async Task InvestorActions_RequireTheInvestorRole_AndCurrentMembership()
    {
        var (investor, request) = await _kit.Submitted();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ModonDbContext>();
            await db.EstablishmentMembers.Where(m => m.EstablishmentId == investor.EstablishmentId).ExecuteDeleteAsync();
        }

        await AssertProblem(await Choose(investor.Client, request.Id, "consultant", SelectionMode.OpenTender),
            HttpStatusCode.Forbidden, ProblemCodes.NotYourEstablishment);
        Assert.Equal(RequestStatus.ConsultantSelection, (await Get(investor.Client, request.Id)).Status);
    }

    // --- monthly progress reports ---------------------------------------------------------------------

    [Fact]
    public async Task ProgressReports_ComeFromTheSelectedOffice_ForApprovedLicences_WithValidatedPercentages()
    {
        var (investor, consultant, contractor, request) = await _kit.UnderReview();
        var reviewer = await _kit.Reviewer();
        var otherConsultant = await _kit.Consultant();
        var month = DateTime.UtcNow.ToString("yyyy-MM");

        await AssertProblem(await Report(consultant.Client, request.Id, month, Progress(10, 0, 0, 0, 0, 0)),
            HttpStatusCode.Conflict, ProblemCodes.InvalidTransition); // not approved yet
        await Body<ReviewRequestDetail>(await Decide(reviewer, request.Id, "approve"));
        Assert.True((await GetProject(consultant.Client, request.Id)).CanReport);

        // Only the selected consulting office reports.
        Assert.Equal(HttpStatusCode.Forbidden, (await Report(contractor.Client, request.Id, month, Progress(1, 1, 1, 1, 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Report(otherConsultant.Client, request.Id, month, Progress(1, 1, 1, 1, 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Report(investor.Client, request.Id, month, Progress(1, 1, 1, 1, 1, 1))).StatusCode);

        var invalid = await Errors(await Report(consultant.Client, request.Id, "2026-13", [.. Progress(101, -1, 50, 50, 50, 50).Take(5)]));
        Assert.Equal(ErrorCodes.InvalidFormat, invalid["period"][0]);
        Assert.Equal(ErrorCodes.OutOfRange, invalid["phases[0].completionPercent"][0]);
        Assert.Equal(ErrorCodes.OutOfRange, invalid["phases[1].completionPercent"][0]);
        Assert.Equal(ErrorCodes.Required, invalid["phases"][0]);
        var future = DateTime.UtcNow.AddMonths(2).ToString("yyyy-MM");
        Assert.Equal(ErrorCodes.FuturePeriod, (await Errors(await Report(consultant.Client, request.Id, future, Progress(0, 0, 0, 0, 0, 0))))["period"][0]);
        Assert.Equal(ErrorCodes.PeriodBeforeApproval, (await Errors(await Report(consultant.Client, request.Id, "2020-01", Progress(0, 0, 0, 0, 0, 0))))["period"][0]);

        // 0 and 100 are valid and the phases do not have to add up to 100.
        var saved = await Project(await Report(consultant.Client, request.Id, month, Progress(100, 100, 35.5m, 0, 0, 0)));
        var report = saved.ProgressReports.Single();
        Assert.Equal((month, consultant.Email, consultant.EstablishmentId), (report.Period, report.SubmittedByEmail, report.Office.Id));
        Assert.Equal([100m, 100m, 35.5m, 0m, 0m, 0m], saved.CurrentProgress.Select(p => p.CompletionPercent!.Value));
        await AssertProblem(await Report(consultant.Client, request.Id, month, Progress(100, 100, 40, 0, 0, 0)),
            HttpStatusCode.Conflict, ProblemCodes.ReportPeriodTaken);

        // The investor and the reviewer see the reports and the history row; the status stays Approved.
        var investorView = await Get(investor.Client, request.Id);
        Assert.Equal((RequestStatus.Approved, 1), (investorView.Status, investorView.Building!.ProgressReports.Length));
        Assert.Equal((RequestAction.SubmitProgressReport, month), (investorView.History[^1].Action, investorView.History[^1].Comment));
        var reviewView = await Body<ReviewRequestDetail>(await reviewer.GetAsync($"/api/modon/review/requests/{request.Id}"));
        Assert.Equal(35.5m, reviewView.Building!.CurrentProgress.Single(p => p.Phase == ConstructionPhase.StructuralWorks).CompletionPercent);
        Assert.Empty(reviewView.AvailableDecisions);
        Assert.Equal(1, await HistoryRows(request.Id, RequestAction.SubmitProgressReport));
    }

    [Fact]
    public async Task ProgressReport_WithAttachment_IsDownloadableByInvestorReviewerAndOffice()
    {
        var (investor, consultant, _, request) = await _kit.UnderReview();
        var reviewer = await _kit.Reviewer();
        await Body<ReviewRequestDetail>(await Decide(reviewer, request.Id, "approve"));
        var body = new ProgressReportBody(DateTime.UtcNow.ToString("yyyy-MM"), Progress(5, 0, 0, 0, 0, 0), null);
        var form = new MultipartFormDataContent
        {
            { new StringContent(System.Text.Json.JsonSerializer.Serialize(body, Json)), "data" },
            { new ByteArrayContent(Pdf) { Headers = { ContentType = new("application/pdf") } }, "file", "report.pdf" },
        };

        var saved = await Project(await consultant.Client.PostAsync($"{ProjectsUrl}/{request.Id}/progress-reports", form));
        var id = saved.ProgressReports.Single().Id;

        Assert.Equal("report.pdf", saved.ProgressReports.Single().Attachment!.FileName);
        Assert.True((await investor.Client.GetAsync($"{RequestsUrl}/{request.Id}/building/progress-reports/{id}/attachment")).IsSuccessStatusCode);
        Assert.True((await reviewer.GetAsync($"/api/modon/review/requests/{request.Id}/building/progress-reports/{id}/attachment")).IsSuccessStatusCode);
        Assert.True((await consultant.Client.GetAsync($"{ProjectsUrl}/{request.Id}/progress-reports/{id}/attachment")).IsSuccessStatusCode);
        var stranger = await _kit.Investor();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"{RequestsUrl}/{request.Id}/building/progress-reports/{id}/attachment")).StatusCode);
    }

    // --- duplicates and races ---------------------------------------------------------------------

    [Fact]
    public async Task RepeatedOrSimultaneousInvestorActions_RecordOneTransition()
    {
        var (investor, request) = await _kit.Submitted();
        var consultant = await _kit.Consultant();

        var opens = await Task.WhenAll(
            Choose(investor.Client, request.Id, "consultant", SelectionMode.OpenTender),
            Choose(investor.Client, request.Id, "consultant", SelectionMode.Direct, consultant.EstablishmentId));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], opens.Select(r => r.StatusCode).Order());
        var after = await Get(investor.Client, request.Id);
        Assert.Single(after.Building!.Tenders);
        Assert.Equal(1, await HistoryRows(request.Id, RequestAction.OpenConsultantTender) + await HistoryRows(request.Id, RequestAction.SelectConsultant));
    }

    [Fact]
    public async Task SimultaneousQuotationChoiceAndRejectAll_LeaveOneOutcome()
    {
        var (investor, request) = await _kit.Submitted();
        var consultant = await _kit.Consultant();
        await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.OpenTender));
        var quote = (await Project(await Quote(consultant.Client, request.Id, consultant.EstablishmentId, 100m))).MyQuotations.Single();

        var results = await Task.WhenAll(
            AcceptQuotation(investor.Client, request.Id, "consultant", quote.Id),
            RejectQuotations(investor.Client, request.Id, "consultant"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Select(r => r.StatusCode).Order());
        var round = (await Get(investor.Client, request.Id)).Building!.Tenders.Single();
        Assert.NotEqual(TenderState.Open, round.State);
        Assert.Equal(round.State == TenderState.Awarded ? QuotationState.Selected : QuotationState.NotSelected, round.Quotations.Single().State);
    }

    [Fact]
    public async Task SimultaneousAnswersOfTheInvitedOffice_RecordOne()
    {
        var (investor, request) = await _kit.Submitted();
        var consultant = await _kit.Consultant();
        await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.Direct, consultant.EstablishmentId));

        var results = await Task.WhenAll(
            Respond(consultant.Client, request.Id, ConsultantAccepts()),
            Respond(consultant.Client, request.Id, new OfficeResponseBody(OfficeDecision.Reject, null, "no")));

        // One answer wins: 200 (accepted) or 204 (rejected); the other is refused.
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Single(results, r => r.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent);
        Assert.Single((await Get(investor.Client, request.Id)).Building!.Tenders.Single().Quotations);
    }

    [Fact]
    public async Task SimultaneousReportsForTheSameMonth_SaveOne()
    {
        var (_, consultant, _, request) = await _kit.UnderReview();
        await Body<ReviewRequestDetail>(await BuildingTestKit.Decide(await _kit.Reviewer(), request.Id, "approve"));
        var month = DateTime.UtcNow.ToString("yyyy-MM");

        var results = await Task.WhenAll(
            Report(consultant.Client, request.Id, month, Progress(10, 0, 0, 0, 0, 0)),
            Report(consultant.Client, request.Id, month, Progress(20, 0, 0, 0, 0, 0)));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Select(r => r.StatusCode).Order());
        Assert.Equal(1, await HistoryRows(request.Id, RequestAction.SubmitProgressReport));
    }
}
