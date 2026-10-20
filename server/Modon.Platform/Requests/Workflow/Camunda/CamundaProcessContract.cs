using System.Reflection;
using System.Xml.Linq;

namespace Modon.Platform.Requests.Workflow.Camunda;

/// <summary>
/// The contract between Modon.Platform and one checked-in BPMN (embedded in the assembly): which service it runs,
/// the message correlated for each action after the start, and which request status each wait state stands for.
/// Message names and element ids must match the BPMN; tests check that for every process.
/// </summary>
public sealed record CamundaProcessContract(
    string ServiceCode,
    string DefaultProcessId,
    string BpmnFileName,
    string BpmnResourceName,
    IReadOnlyDictionary<RequestAction, string> Messages,
    IReadOnlyDictionary<string, RequestStatus> StatusByElement)
{
    /// <summary>The checked-in BPMN, exactly as it should be deployed.</summary>
    public string LoadBpmn()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BpmnResourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {BpmnResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The checked-in BPMN's <c>zeebe:versionTag</c>, to compare with the deployed one.</summary>
    public string? CheckedInVersionTag()
    {
        XNamespace zeebe = "http://camunda.org/schema/zeebe/1.0";
        return XDocument.Parse(LoadBpmn()).Descendants(zeebe + "versionTag").FirstOrDefault()?.Attribute("value")?.Value;
    }
}

/// <summary>A process contract with the process id configured for this installation.</summary>
public sealed record ConfiguredProcess(CamundaProcessContract Contract, string ProcessId);

/// <summary>The processes Modon drives in Camunda, one per service (from <see cref="WorkflowOptions"/>).</summary>
public sealed class CamundaProcesses
{
    public IReadOnlyList<ConfiguredProcess> All { get; }

    public CamundaProcesses(WorkflowOptions options)
        => All =
        [
            new ConfiguredProcess(ExcavationProcess.Contract, options.Camunda.ProcessId),
            new ConfiguredProcess(BuildingLicenceProcess.Contract, options.Camunda.BuildingLicenceProcessId),
        ];

    public ConfiguredProcess? ForService(string serviceCode) => All.FirstOrDefault(p => p.Contract.ServiceCode == serviceCode);

    public ConfiguredProcess? ForProcessId(string processId) => All.FirstOrDefault(p => p.ProcessId == processId);
}
