using System.Reflection;
using System.Xml.Linq;

namespace Modon.Platform.Requests.Workflow.Camunda;

/// <summary>
/// The contract between Modon.Platform and <c>excavation-permit.bpmn</c> (checked in next to this file and
/// embedded in the assembly). Message names and element ids here must match the BPMN; a test checks that.
/// </summary>
public static class ExcavationProcess
{
    public const string DefaultProcessId = "modon-excavation-permit";

    /// <summary>Process variable every message subscription correlates on (unique per request submission).</summary>
    public const string CorrelationKeyVariable = "correlationKey";

    public const string BpmnResourceName = "Modon.Platform.excavation-permit.bpmn";

    /// <summary>The message Modon.Platform correlates for each action after the process has started.</summary>
    public static readonly IReadOnlyDictionary<RequestAction, string> Messages = new Dictionary<RequestAction, string>
    {
        [RequestAction.Approve] = "ExcavationApproved",
        [RequestAction.Reject] = "ExcavationRejected",
        [RequestAction.ReturnForCompletion] = "ExcavationReturnedForCompletion",
        [RequestAction.RequestCoordinationLetters] = "ExcavationCoordinationLettersRequested",
        [RequestAction.Resubmit] = "ExcavationResubmitted",
        [RequestAction.SubmitCoordinationLetters] = "ExcavationCoordinationLettersSubmitted",
    };

    /// <summary>
    /// Where an instance waits (or ended) for each request status. Used to read the engine's view of a
    /// request (diagnostics, and recovery when a step reached Camunda but was not saved to the database).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, RequestStatus> StatusByElement = new Dictionary<string, RequestStatus>
    {
        ["Gateway_ModonReview"] = RequestStatus.InProgress,
        ["Event_AwaitResubmission"] = RequestStatus.ReturnedForCompletion,
        ["Event_AwaitCoordinationLetters"] = RequestStatus.AwaitingCoordinationLetters,
        ["End_Approved"] = RequestStatus.Approved,
        ["End_Rejected"] = RequestStatus.Rejected,
    };

    /// <summary>The checked-in BPMN, exactly as it should be deployed.</summary>
    public static string LoadBpmn()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BpmnResourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {BpmnResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The checked-in BPMN's <c>zeebe:versionTag</c>, to compare with the deployed one.</summary>
    public static string? CheckedInVersionTag()
    {
        XNamespace zeebe = "http://camunda.org/schema/zeebe/1.0";
        return XDocument.Parse(LoadBpmn()).Descendants(zeebe + "versionTag").FirstOrDefault()?.Attribute("value")?.Value;
    }
}
