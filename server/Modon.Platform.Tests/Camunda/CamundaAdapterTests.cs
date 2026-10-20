using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Modon.Platform.Requests;
using Modon.Platform.Requests.Workflow;
using Modon.Platform.Requests.Workflow.Camunda;

namespace Modon.Platform.Tests.Camunda;

/// <summary>The checked-in BPMN, the adapter's contract with it, and the adapter's connection handling.</summary>
public class CamundaAdapterTests
{
    private static WorkflowOptions Options(string auth = CamundaWorkflowOptions.AuthNone, string? user = null, string? password = null) => new()
    {
        Engine = WorkflowEngineKind.Camunda,
        Camunda = new CamundaWorkflowOptions
        {
            RestAddress = FakeCamunda.Address, AuthStrategy = auth, BasicUsername = user, BasicPassword = password,
            RequestTimeoutSeconds = 1,
        },
    };

    private static CamundaSdkEngine Engine(FakeCamunda fake, WorkflowOptions? options = null)
        => new(options ?? Options(), NullLoggerFactory.Instance, fake);

    // --- the BPMN ------------------------------------------------------------------------------

    [Fact]
    public void Bpmn_DefinesEveryMessageAndElementTheAdapterUses()
    {
        var model = BpmnModel.Load();

        Assert.Equal(ExcavationProcess.DefaultProcessId, model.ProcessId);
        Assert.Equal(ExcavationProcess.CheckedInVersionTag(), model.VersionTag);
        Assert.NotNull(model.VersionTag);
        foreach (var message in ExcavationProcess.Messages.Values)
            Assert.Equal("=" + ExcavationProcess.CorrelationKeyVariable, model.Messages[message]); // every subscription uses correlationKey
        Assert.Equal(ExcavationProcess.Messages.Values.Order(), model.Messages.Keys.Order());
        foreach (var element in ExcavationProcess.StatusByElement.Keys) Assert.True(model.Elements.ContainsKey(element), element);
        // The adapter maps every action of the excavation lifecycle except the first submission (which starts the process).
        var rules = new InProcessRequestWorkflow();
        var mapped = Enum.GetValues<RequestAction>()
            .Where(a => Enum.GetValues<RequestStatus>().Any(s => rules.Allows(ExcavationProcess.Contract.ServiceCode, s, a)))
            .Except([RequestAction.Submit]);
        Assert.Equal(mapped.Order(), ExcavationProcess.Messages.Keys.Order());
    }

    [Fact]
    public void Bpmn_PathsMatchTheRequestLifecycle()
    {
        var model = BpmnModel.Load();
        string WaitAfter(string from) => model.RunFrom(from)[^1].Id;
        string Deliver(string wait, string message) => WaitAfter(model.AcceptedMessages(wait)[message].Id);

        var review = WaitAfter(model.StartEventId);
        Assert.Equal("Gateway_ModonReview", review);
        Assert.Equal(
            ["ExcavationApproved", "ExcavationCoordinationLettersRequested", "ExcavationRejected", "ExcavationReturnedForCompletion"],
            model.AcceptedMessages(review).Keys.Order());

        Assert.Equal("End_Approved", Deliver(review, "ExcavationApproved"));
        Assert.Equal("End_Rejected", Deliver(review, "ExcavationRejected"));

        var returned = Deliver(review, "ExcavationReturnedForCompletion");
        Assert.Equal("Event_AwaitResubmission", returned);
        Assert.Equal(["ExcavationResubmitted"], model.AcceptedMessages(returned).Keys);
        Assert.Equal(review, Deliver(returned, "ExcavationResubmitted"));

        var letters = Deliver(review, "ExcavationCoordinationLettersRequested");
        Assert.Equal("Event_AwaitCoordinationLetters", letters);
        Assert.Equal(["ExcavationCoordinationLettersSubmitted"], model.AcceptedMessages(letters).Keys);
        Assert.Equal(review, Deliver(letters, "ExcavationCoordinationLettersSubmitted"));

        // The wait states map to the statuses the in-process rules lead to.
        var rules = new InProcessRequestWorkflow();
        Assert.Equal(rules.NextStatus(RequestStatus.Draft, RequestAction.Submit), ExcavationProcess.StatusByElement[review]);
        Assert.Equal(rules.NextStatus(RequestStatus.InProgress, RequestAction.ReturnForCompletion), ExcavationProcess.StatusByElement[returned]);
        Assert.Equal(rules.NextStatus(RequestStatus.InProgress, RequestAction.RequestCoordinationLetters), ExcavationProcess.StatusByElement[letters]);
    }

    [Fact]
    public void Bpmn_ElementNamesAreAscii_BecauseTheLocalClusterGarblesOthers()
    {
        var names = System.Text.RegularExpressions.Regex.Matches(ExcavationProcess.LoadBpmn(), "name=\"([^\"]*)\"").Select(m => m.Groups[1].Value);
        Assert.All(names, n => Assert.True(Encoding.UTF8.GetByteCount(n) == n.Length, n));
    }

    // --- connection handling ------------------------------------------------------------------

    [Fact]
    public async Task BasicAuth_SendsTheConfiguredCredentials_AndNoneSendsNone()
    {
        var fake = new FakeCamunda();
        using (var basic = Engine(fake, Options(CamundaWorkflowOptions.AuthBasic, "demo", "demo")))
            await basic.DescribeAsync(CancellationToken.None);
        var basicCalls = fake.Calls.ToArray();
        fake.Calls.Clear();
        using (var none = Engine(fake))
            await none.DescribeAsync(CancellationToken.None);

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("demo:demo"));
        Assert.NotEmpty(basicCalls);
        Assert.All(basicCalls, c => Assert.Equal(expected, c.Authorization));
        Assert.All(fake.Calls, c => Assert.Null(c.Authorization));
    }

    [Fact]
    public async Task Describe_ReportsAnUnreachableCluster_WithTheAddress()
    {
        var fake = new FakeCamunda { Down = true };
        using var engine = Engine(fake);

        var report = await engine.DescribeAsync(CancellationToken.None);

        Assert.False(report.Reachable);
        Assert.Contains(FakeCamunda.Address, report.Error);
        Assert.Contains("actively refused", report.Error);
    }

    [Fact]
    public async Task ASlowCluster_TimesOut_AsUnavailable()
    {
        var fake = new FakeCamunda { Delay = TimeSpan.FromSeconds(5) };
        using var engine = Engine(fake);

        var error = await Assert.ThrowsAsync<WorkflowUnavailableException>(() =>
            engine.CorrelateAsync("ExcavationApproved", "k", new Dictionary<string, object?>(), CancellationToken.None));

        Assert.Contains("did not answer", error.Message);
    }

    [Fact]
    public async Task RefusedCredentials_AreReportedWithAHint()
    {
        var fake = new FakeCamunda();
        fake.FailNext["/v2/process-instances"] = HttpStatusCode.Unauthorized;
        using var engine = Engine(fake);

        var error = await Assert.ThrowsAsync<WorkflowUnavailableException>(() =>
            engine.StartAsync("EXC-1", "EXC-1:k", new Dictionary<string, object?> { ["correlationKey"] = "EXC-1:k" }, CancellationToken.None));

        Assert.Contains("401", error.Message);
        Assert.Contains("AuthStrategy", error.Message);
    }

    [Fact]
    public async Task ANotDeployedProcess_IsReportedAsSuch()
    {
        var fake = new FakeCamunda { Deployed = false };
        using var engine = Engine(fake);

        var error = await Assert.ThrowsAsync<WorkflowUnavailableException>(() =>
            engine.StartAsync("EXC-1", "EXC-1:k", new Dictionary<string, object?>(), CancellationToken.None));

        Assert.Contains("is not deployed", error.Message);
        Assert.Contains(ExcavationProcess.DefaultProcessId, error.Message);
    }

    /// <summary>
    /// Development defaults: Camunda is opt-in (launch profile "http-camunda"), and its address uses the IPv4
    /// loopback — "localhost" hits the same Docker Desktop IPv6 hang seen with PostgreSQL (checked live on 8080).
    /// </summary>
    [Fact]
    public void DevelopmentSettings_KeepCamundaOptIn_AndUseIpv4Loopback()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json"), optional: false)
            .Build();
        var options = new WorkflowOptions();
        config.GetSection(WorkflowOptions.Section).Bind(options);

        Assert.Equal(WorkflowEngineKind.InProcess, options.Engine);
        Assert.Equal("127.0.0.1", new Uri(options.Camunda.RestAddress).Host);
        Assert.Equal(ExcavationProcess.DefaultProcessId, options.Camunda.ProcessId);
        new WorkflowOptions { Engine = WorkflowEngineKind.Camunda, Camunda = options.Camunda }.Validate();
    }

    [Theory]
    [InlineData("", "NONE", null, null, "RestAddress")]
    [InlineData("localhost:8080", "NONE", null, null, "RestAddress")]
    [InlineData("http://127.0.0.1:8080", "OAUTH", null, null, "AuthStrategy")]
    [InlineData("http://127.0.0.1:8080", "BASIC", "demo", null, "BasicPassword")]
    public void InvalidCamundaSettings_FailAtStartup_WithAReadableMessage(string address, string auth, string? user, string? password, string mentions)
    {
        var options = new WorkflowOptions
        {
            Engine = WorkflowEngineKind.Camunda,
            Camunda = new CamundaWorkflowOptions { RestAddress = address, AuthStrategy = auth, BasicUsername = user, BasicPassword = password },
        };

        Assert.Contains(mentions, Assert.Throws<InvalidOperationException>(options.Validate).Message);
        new WorkflowOptions { Engine = WorkflowEngineKind.InProcess, Camunda = options.Camunda }.Validate(); // not checked when off
    }
}

/// <summary>The default engine is the in-process one, and it says so.</summary>
[Collection("Modon API")]
public class InProcessWorkflowDiagnosticsTests(ModonAppFactory factory)
{
    [Fact]
    public async Task DefaultEngine_IsInProcess_AndReportsCamundaAsDisabled()
    {
        var reviewer = await factory.LoginAs("reviewer@modon.local");

        var report = await reviewer.GetFromJsonAsync<WorkflowStatusReport>("/api/modon/review/workflow", RequestTestKit.Json);

        Assert.Equal(("inProcess", true, (CamundaClusterReport?)null), (report!.Engine, report.Ready, report.Camunda));
        Assert.Contains("Camunda is disabled", report.Summary);
        Assert.Equal(HttpStatusCode.OK, (await factory.NewClient().GetAsync("/health")).StatusCode);
    }
}
