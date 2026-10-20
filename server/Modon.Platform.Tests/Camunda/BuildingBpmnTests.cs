using System.Text;
using Modon.Platform.Requests;
using Modon.Platform.Requests.Workflow;
using Modon.Platform.Requests.Workflow.Camunda;

namespace Modon.Platform.Tests.Camunda;

/// <summary>The checked-in building-licence.bpmn against the adapter's contract and the in-process rules.</summary>
public class BuildingBpmnTests
{
    private static readonly CamundaProcessContract Contract = BuildingLicenceProcess.Contract;
    private static readonly InProcessRequestWorkflow Rules = new();

    private static IEnumerable<(RequestStatus From, RequestAction Action)> BuildingTransitions()
        => from s in Enum.GetValues<RequestStatus>()
           from a in Enum.GetValues<RequestAction>()
           where Rules.Allows(Contract.ServiceCode, s, a)
           select (s, a);

    [Fact]
    public void Bpmn_IsASeparateProcess_DefiningEveryMessageAndElementTheAdapterUses()
    {
        var model = BpmnModel.Load(Contract);

        Assert.Equal(BuildingLicenceProcess.DefaultProcessId, model.ProcessId);
        Assert.NotEqual(ExcavationProcess.DefaultProcessId, model.ProcessId);
        Assert.NotNull(model.VersionTag);
        Assert.Equal(Contract.CheckedInVersionTag(), model.VersionTag);
        foreach (var message in Contract.Messages.Values)
            Assert.Equal("=" + ExcavationProcess.CorrelationKeyVariable, model.Messages[message]);
        Assert.Equal(Contract.Messages.Values.Order(), model.Messages.Keys.Order());
        Assert.Empty(Contract.Messages.Values.Intersect(ExcavationProcess.Messages.Values)); // message names never collide
        foreach (var element in Contract.StatusByElement.Keys) Assert.True(model.Elements.ContainsKey(element), element);

        // Every action of the building lifecycle except the first submission (which starts the process) is a message.
        var mapped = BuildingTransitions().Select(t => t.Action).Distinct().Except([RequestAction.Submit]);
        Assert.Equal(mapped.Order(), Contract.Messages.Keys.Order());
    }

    [Fact]
    public void Bpmn_WaitStates_AcceptExactlyTheActionsTheRulesAllow_AndLeadToTheSameStatus()
    {
        var model = BpmnModel.Load(Contract);
        var waitOf = Contract.StatusByElement.Where(e => model.Elements[e.Key].Type != "END_EVENT")
            .ToDictionary(e => e.Value, e => e.Key);
        string After(string fromId) => model.RunFrom(fromId)[^1].Id;

        Assert.Equal(Rules.NextStatus(Contract.ServiceCode, RequestStatus.Draft, RequestAction.Submit),
            Contract.StatusByElement[After(model.StartEventId)]);

        foreach (var (status, wait) in waitOf)
        {
            var allowed = BuildingTransitions().Where(t => t.From == status).Select(t => Contract.Messages[t.Action]);
            Assert.Equal(allowed.Order(), model.AcceptedMessages(wait).Keys.Order());
        }
        foreach (var (from, action) in BuildingTransitions().Where(t => t.Action != RequestAction.Submit))
        {
            var catchEvent = model.AcceptedMessages(waitOf[from])[Contract.Messages[action]];
            Assert.Equal(Rules.NextStatus(Contract.ServiceCode, from, action), Contract.StatusByElement[After(catchEvent.Id)]);
        }
    }

    [Fact]
    public void Bpmn_ElementNamesAreAscii_BecauseTheLocalClusterGarblesOthers()
    {
        var names = System.Text.RegularExpressions.Regex.Matches(Contract.LoadBpmn(), "name=\"([^\"]*)\"").Select(m => m.Groups[1].Value);
        Assert.All(names, n => Assert.True(Encoding.UTF8.GetByteCount(n) == n.Length, n));
    }

    [Fact]
    public void Settings_RequireADistinctBuildingProcessId()
    {
        var options = new WorkflowOptions
        {
            Engine = WorkflowEngineKind.Camunda,
            Camunda = new CamundaWorkflowOptions
            {
                RestAddress = "http://127.0.0.1:8080", BuildingLicenceProcessId = ExcavationProcess.DefaultProcessId,
            },
        };
        Assert.Contains("BuildingLicenceProcessId", Assert.Throws<InvalidOperationException>(options.Validate).Message);
        options.Camunda.BuildingLicenceProcessId = " ";
        Assert.Contains("BuildingLicenceProcessId", Assert.Throws<InvalidOperationException>(options.Validate).Message);
        options.Camunda.BuildingLicenceProcessId = BuildingLicenceProcess.DefaultProcessId;
        options.Validate();
    }
}
