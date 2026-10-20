using Modon.Platform.Catalog;

namespace Modon.Platform.Requests.Workflow.Camunda;

/// <summary>
/// The contract between Modon.Platform and <c>building-licence.bpmn</c> (checked in next to this file and embedded
/// in the assembly). A separate process with its own id; the excavation process is not affected.
/// The process starts when the investor submits the basic information and then waits, at event-based gateways,
/// for the investor's stage choices, the offices' answers, Modon's decision and the monthly progress reports.
/// </summary>
public static class BuildingLicenceProcess
{
    public const string DefaultProcessId = "modon-building-licence";

    public const string BpmnResourceName = "Modon.Platform.building-licence.bpmn";

    public static readonly IReadOnlyDictionary<RequestAction, string> Messages = new Dictionary<RequestAction, string>
    {
        [RequestAction.OpenConsultantTender] = "BuildingConsultantTenderOpened",
        [RequestAction.SelectConsultant] = "BuildingConsultantSelected",
        [RequestAction.AcceptConsultantQuotation] = "BuildingConsultantQuotationAccepted",
        [RequestAction.RejectConsultantQuotations] = "BuildingConsultantQuotationsRejected",
        [RequestAction.ConsultantAccepted] = "BuildingConsultantAccepted",
        [RequestAction.ConsultantDeclined] = "BuildingConsultantDeclined",
        [RequestAction.OpenContractorTender] = "BuildingContractorTenderOpened",
        [RequestAction.SelectContractor] = "BuildingContractorSelected",
        [RequestAction.AcceptContractorQuotation] = "BuildingContractorQuotationAccepted",
        [RequestAction.RejectContractorQuotations] = "BuildingContractorQuotationsRejected",
        [RequestAction.ContractorAccepted] = "BuildingContractorAccepted",
        [RequestAction.ContractorDeclined] = "BuildingContractorDeclined",
        [RequestAction.Approve] = "BuildingLicenceApproved",
        [RequestAction.Reject] = "BuildingLicenceRejected",
        [RequestAction.SubmitProgressReport] = "BuildingProgressReportSubmitted",
    };

    /// <summary>Where an instance waits (or ended) for each request status.</summary>
    public static readonly IReadOnlyDictionary<string, RequestStatus> StatusByElement = new Dictionary<string, RequestStatus>
    {
        ["Gateway_ConsultantSelection"] = RequestStatus.ConsultantSelection,
        ["Gateway_ConsultantTender"] = RequestStatus.ConsultantTender,
        ["Gateway_ConsultantDecision"] = RequestStatus.AwaitingConsultantDecision,
        ["Gateway_ContractorSelection"] = RequestStatus.ContractorSelection,
        ["Gateway_ContractorTender"] = RequestStatus.ContractorTender,
        ["Gateway_ContractorDecision"] = RequestStatus.AwaitingContractorDecision,
        ["Gateway_LicenceReview"] = RequestStatus.UnderModonReview,
        // After approval the instance stays here, receiving one message per monthly progress report.
        ["Event_AwaitProgressReport"] = RequestStatus.Approved,
        ["End_LicenceRejected"] = RequestStatus.Rejected,
    };

    public static readonly CamundaProcessContract Contract = new(
        ServiceCatalogData.BuildingLicenceCode, DefaultProcessId, "building-licence.bpmn", BpmnResourceName,
        Messages, StatusByElement);
}
