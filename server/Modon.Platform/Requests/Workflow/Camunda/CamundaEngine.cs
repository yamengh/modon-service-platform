using Camunda.Orchestration.Sdk;

namespace Modon.Platform.Requests.Workflow.Camunda;

/// <summary>
/// The few Camunda operations Modon needs, in Modon's terms. <see cref="CamundaSdkEngine"/> implements it with
/// the Camunda 8.9 C# SDK; tests use the same class against a fake HTTP server.
/// Technical failures surface as <see cref="WorkflowUnavailableException"/> with a readable message.
/// </summary>
public interface ICamundaEngine
{
    string RestAddress { get; }

    /// <summary>Starts the latest deployed version of a process (one of <see cref="CamundaProcesses"/>).</summary>
    Task<WorkflowInstanceLink> StartAsync(string processId, string businessId, string correlationKey,
        IReadOnlyDictionary<string, object?> variables, CancellationToken ct);

    /// <summary>
    /// Correlates a message. Returns the process instance it was delivered to, or <c>null</c> when no instance
    /// is waiting for this message with this correlation key (the step is not possible in the process now).
    /// </summary>
    Task<string?> CorrelateAsync(string messageName, string correlationKey,
        IReadOnlyDictionary<string, object?> variables, CancellationToken ct);

    /// <summary>Cancels an instance; an instance that no longer exists is not an error.</summary>
    Task CancelAsync(string processInstanceKey, CancellationToken ct);

    /// <summary>
    /// Where an instance currently is (from Camunda's search API, which may lag by about a second), read with the
    /// element map of the process the instance belongs to.
    /// </summary>
    Task<EngineInstanceView> GetInstanceAsync(string processInstanceKey, CancellationToken ct);

    /// <summary>Connection and deployment diagnostics. Never throws for connection problems.</summary>
    Task<CamundaClusterReport> DescribeAsync(CancellationToken ct);
}

/// <param name="State">ACTIVE, COMPLETED, TERMINATED (cancelled) or NOT_FOUND.</param>
/// <param name="Status">The request status the instance's current element stands for, if known.</param>
/// <param name="ElementId">The element it waits at, or the end event it finished at.</param>
public sealed record EngineInstanceView(
    string ProcessInstanceKey,
    string State,
    RequestStatus? Status,
    string? ElementId,
    DateTimeOffset? ElementSince,
    int? ProcessDefinitionVersion,
    bool HasIncident);

/// <summary>
/// Connection diagnostics. The top-level process fields describe the excavation process (as before the building
/// licence was added); <see cref="Processes"/> lists every process Modon drives, each with its own deployment state.
/// </summary>
public sealed record CamundaClusterReport(
    string RestAddress,
    string AuthStrategy,
    bool Reachable,
    string? GatewayVersion,
    int? Brokers,
    bool PartitionsHealthy,
    string ProcessId,
    bool ProcessDeployed,
    int? DeployedVersion,
    string? DeployedVersionTag,
    string? CheckedInVersionTag,
    string? ProcessDefinitionKey,
    string? Error,
    IReadOnlyList<CamundaProcessReport> Processes);

/// <summary>One process: whether the configured id is deployed, and which version against the checked-in BPMN.</summary>
public sealed record CamundaProcessReport(
    string ServiceCode,
    string ProcessId,
    string BpmnFile,
    bool Deployed,
    int? DeployedVersion,
    string? DeployedVersionTag,
    string? CheckedInVersionTag,
    string? ProcessDefinitionKey);

public sealed class CamundaSdkEngine : ICamundaEngine, IDisposable
{
    private readonly CamundaClient _client;
    private readonly CamundaWorkflowOptions _options;
    private readonly CamundaProcesses _processes;
    private readonly ILogger<CamundaSdkEngine> _logger;

    /// <param name="handler">Only for tests (a fake cluster) or proxies; null uses a normal HTTP connection.</param>
    public CamundaSdkEngine(WorkflowOptions options, ILoggerFactory loggerFactory, HttpMessageHandler? handler = null)
    {
        _options = options.Camunda;
        _processes = new CamundaProcesses(options);
        _logger = loggerFactory.CreateLogger<CamundaSdkEngine>();

        var config = new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = _options.RestAddress, // the SDK appends /v2
            ["CAMUNDA_AUTH_STRATEGY"] = _options.AuthStrategy,
            // One attempt per call. The SDK would otherwise retry on timeouts and 5xx, and retrying
            // "create process instance" after a lost response could start a second instance for one request.
            ["CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS"] = "1",
        };
        if (_options.AuthStrategy == CamundaWorkflowOptions.AuthBasic)
        {
            config["CAMUNDA_BASIC_AUTH_USERNAME"] = _options.BasicUsername!;
            config["CAMUNDA_BASIC_AUTH_PASSWORD"] = _options.BasicPassword!;
        }
        if (!string.IsNullOrWhiteSpace(_options.TenantId)) config["CAMUNDA_DEFAULT_TENANT_ID"] = _options.TenantId;

        _client = CamundaClient.Create(new CamundaOptions
        {
            Config = config,
            Env = [], // use only Modon's settings, not CAMUNDA_* variables that happen to be set on the machine
            HttpMessageHandler = handler,
            LoggerFactory = loggerFactory,
        });
    }

    public string RestAddress => _options.RestAddress;

    /// <summary>Starts the excavation process (the configured <see cref="CamundaWorkflowOptions.ProcessId"/>).</summary>
    public Task<WorkflowInstanceLink> StartAsync(string businessId, string correlationKey,
        IReadOnlyDictionary<string, object?> variables, CancellationToken ct)
        => StartAsync(_options.ProcessId, businessId, correlationKey, variables, ct);

    public async Task<WorkflowInstanceLink> StartAsync(string processId, string businessId, string correlationKey,
        IReadOnlyDictionary<string, object?> variables, CancellationToken ct)
    {
        try
        {
            var created = await CallAsync("create process instance", t => _client.CreateProcessInstanceAsync(
                new ProcessInstanceCreationInstructionById
                {
                    ProcessDefinitionId = ProcessDefinitionId.AssumeExists(processId),
                    BusinessId = BusinessId.AssumeExists(businessId),
                    Variables = variables,
                }, t), ct);
            return new WorkflowInstanceLink(created.ProcessInstanceKey.Value, correlationKey,
                created.ProcessDefinitionKey.Value, created.ProcessDefinitionVersion);
        }
        catch (NotFoundException e)
        {
            var file = _processes.ForProcessId(processId)?.Contract.BpmnFileName ?? "its BPMN";
            throw new WorkflowUnavailableException(
                $"Process '{processId}' is not deployed in Camunda at {_options.RestAddress}. " +
                $"Deploy {file} first (see MODON.md). Camunda said: {e.Message}");
        }
    }

    public async Task<string?> CorrelateAsync(string messageName, string correlationKey,
        IReadOnlyDictionary<string, object?> variables, CancellationToken ct)
    {
        try
        {
            var result = await CallAsync($"correlate message {messageName}", t => _client.CorrelateMessageAsync(
                new MessageCorrelationRequest { Name = messageName, CorrelationKey = correlationKey, Variables = variables }, t), ct);
            return result.ProcessInstanceKey.Value;
        }
        catch (NotFoundException)
        {
            return null; // no instance is waiting for this message
        }
    }

    public async Task CancelAsync(string processInstanceKey, CancellationToken ct)
    {
        try
        {
            await CallAsync("cancel process instance", async t =>
            {
                await _client.CancelProcessInstanceAsync(ProcessInstanceKey.AssumeExists(processInstanceKey),
                    new CancelProcessInstanceRequest(), t);
                return true;
            }, ct);
        }
        catch (NotFoundException)
        {
            // already finished or cancelled
        }
    }

    public async Task<EngineInstanceView> GetInstanceAsync(string processInstanceKey, CancellationToken ct)
    {
        var key = ProcessInstanceKey.AssumeExists(processInstanceKey);
        ProcessInstanceResult instance;
        try
        {
            instance = await CallAsync("get process instance", t => _client.GetProcessInstanceAsync(key, ct: t), ct);
        }
        catch (NotFoundException)
        {
            return new EngineInstanceView(processInstanceKey, "NOT_FOUND", null, null, null, null, false);
        }

        var elements = await CallAsync("search element instances", t => _client.SearchElementInstancesAsync(
            new ElementInstanceSearchQuery { Filter = new ElementInstanceFilter { ProcessInstanceKey = key } }, ct: t), ct);

        // A running instance: the element it waits at. A finished one: the end event it reached.
        var statusByElement = _processes.ForProcessId(instance.ProcessDefinitionId.Value)?.Contract.StatusByElement
                              ?? new Dictionary<string, RequestStatus>();
        var active = instance.State == ProcessInstanceStateEnum.ACTIVE;
        var current = elements.Items
            .Where(e => statusByElement.ContainsKey(e.ElementId.Value))
            .Where(e => active ? e.State == ElementInstanceStateEnum.ACTIVE
                               : e.State == ElementInstanceStateEnum.COMPLETED && e.Type == "END_EVENT")
            .OrderByDescending(e => e.StartDate)
            .FirstOrDefault();

        return new EngineInstanceView(
            processInstanceKey,
            instance.State.ToString(),
            current is null ? null : statusByElement[current.ElementId.Value],
            current?.ElementId.Value,
            current?.StartDate,
            instance.ProcessDefinitionVersion,
            instance.HasIncident);
    }

    public async Task<CamundaClusterReport> DescribeAsync(CancellationToken ct)
    {
        CamundaClusterReport Report(bool reachable, TopologyResponse? topology, IReadOnlyList<CamundaProcessReport> processes, string? error)
        {
            var excavation = processes.FirstOrDefault(p => p.ServiceCode == ExcavationProcess.Contract.ServiceCode);
            return new CamundaClusterReport(
                _options.RestAddress, _options.AuthStrategy, reachable,
                topology?.GatewayVersion, topology?.Brokers?.Count,
                topology?.Brokers?.All(b => b.Partitions?.All(p => p.Health == "healthy") ?? false) ?? false,
                _options.ProcessId, excavation?.Deployed ?? false, excavation?.DeployedVersion, excavation?.DeployedVersionTag,
                ExcavationProcess.CheckedInVersionTag(), excavation?.ProcessDefinitionKey, error, processes);
        }
        CamundaProcessReport[] NotChecked() => _processes.All.Select(p => new CamundaProcessReport(
            p.Contract.ServiceCode, p.ProcessId, p.Contract.BpmnFileName, false, null, null, p.Contract.CheckedInVersionTag(), null)).ToArray();

        TopologyResponse topology;
        try
        {
            topology = await CallAsync("read cluster topology", t => _client.GetTopologyAsync(t), ct);
        }
        catch (Exception e) when (e is WorkflowUnavailableException or NotFoundException)
        {
            return Report(false, null, NotChecked(), e.Message);
        }

        try
        {
            var reports = new List<CamundaProcessReport>();
            foreach (var process in _processes.All)
            {
                var definitions = await CallAsync("search process definitions", t => _client.SearchProcessDefinitionsAsync(
                    new ProcessDefinitionSearchQuery
                    {
                        Filter = new ProcessDefinitionFilter
                        {
                            ProcessDefinitionId = new StringFilterProperty { Eq = process.ProcessId },
                            IsLatestVersion = true,
                        },
                    }, ct: t), ct);
                var latest = definitions.Items.OrderByDescending(d => d.Version).FirstOrDefault();
                reports.Add(new CamundaProcessReport(process.Contract.ServiceCode, process.ProcessId, process.Contract.BpmnFileName,
                    latest is not null, latest?.Version, latest?.VersionTag, process.Contract.CheckedInVersionTag(),
                    latest?.ProcessDefinitionKey.Value));
            }
            var missing = reports.Where(p => !p.Deployed).ToArray();
            return Report(true, topology, reports, missing.Length == 0
                ? null
                : string.Join(" ", missing.Select(p => $"Process '{p.ProcessId}' is not deployed. Deploy {p.BpmnFile} (see MODON.md).")));
        }
        catch (Exception e) when (e is WorkflowUnavailableException or NotFoundException)
        {
            return Report(true, topology, NotChecked(), e.Message);
        }
    }

    public void Dispose() => _client.Dispose();

    /// <summary>A 404 from Camunda: the caller decides what it means (no subscription, not deployed, gone).</summary>
    private sealed class NotFoundException(string message) : Exception(message);

    private async Task<T> CallAsync<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        try
        {
            return await call(timeout.Token);
        }
        catch (HttpSdkException e) when (e.Status == 404)
        {
            throw new NotFoundException(e.Detail ?? e.Message);
        }
        catch (HttpSdkException e)
        {
            var hint = e.Status is 401 or 403
                ? $" Check Modon:Workflow:Camunda:AuthStrategy ({_options.AuthStrategy}) and the credentials."
                : "";
            _logger.LogWarning("Camunda refused {Operation}: {Status} {Title} {Detail}", operation, e.Status, e.Title, e.Detail);
            throw new WorkflowUnavailableException(
                $"Camunda at {_options.RestAddress} refused to {operation}: HTTP {e.Status} {e.Title} {e.Detail}.{hint}".Trim(), e);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new WorkflowUnavailableException(
                $"Camunda at {_options.RestAddress} did not answer ({operation}) within {_options.RequestTimeoutSeconds} s.", e);
        }
        catch (Exception e) when (e is HttpRequestException or CamundaSdkException)
        {
            _logger.LogWarning(e, "Camunda call failed: {Operation}", operation);
            throw new WorkflowUnavailableException(
                $"Cannot reach Camunda at {_options.RestAddress} ({operation}): {Innermost(e).Message}", e);
        }
    }

    private static Exception Innermost(Exception e) => e.InnerException is null ? e : Innermost(e.InnerException);
}
