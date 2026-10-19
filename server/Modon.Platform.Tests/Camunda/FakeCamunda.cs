using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Modon.Platform.Tests.Camunda;

/// <summary>
/// An in-memory stand-in for the Camunda 8.9 Orchestration Cluster REST API (<c>/v2</c>), used as the SDK's
/// HTTP handler. Response shapes copy what the local 8.9.21 cluster returned; process behaviour comes from
/// the checked-in BPMN (<see cref="BpmnModel"/>). Faults can be injected to test failure handling.
/// Live behaviour is checked separately against the real cluster (see MODON.md).
/// </summary>
public sealed class FakeCamunda : HttpMessageHandler
{
    public const string Address = "http://camunda.test:8080";
    public const string GatewayVersion = "8.9.21";
    public const string DefinitionKey = "2251799813700001";

    private readonly BpmnModel _model = BpmnModel.Load();
    private readonly object _lock = new();
    private readonly Dictionary<string, Instance> _instances = new();
    private long _nextKey = 2251799813710000;

    public sealed class Visit
    {
        public required string Key { get; init; }
        public required string ElementId { get; init; }
        public required string Type { get; init; }
        public required DateTimeOffset Start { get; init; }
        public DateTimeOffset? End { get; set; }
        public string State => End is null ? "ACTIVE" : "COMPLETED";
    }

    public sealed class Instance
    {
        public required string Key { get; init; }
        public required string BusinessId { get; init; }
        public required JsonObject Variables { get; init; }
        public required DateTimeOffset Started { get; init; }
        public string State { get; set; } = "ACTIVE";
        public List<Visit> Visits { get; } = [];
        public string? CorrelationKey => (string?)Variables["correlationKey"];
        public Visit? Waiting => State == "ACTIVE" ? Visits.LastOrDefault(v => v.End is null) : null;
        public string CurrentElement => Waiting?.ElementId ?? Visits.Last().ElementId;
    }

    public sealed record Call(string Method, string Path, string? Body, string? Authorization);

    // --- knobs ------------------------------------------------------------------------------------

    /// <summary>When true every call fails like an unreachable host.</summary>
    public bool Down { get; set; }

    /// <summary>When false the process is "not deployed": create returns 404, the definition search is empty.</summary>
    public bool Deployed { get; set; } = true;

    /// <summary>Next call whose path contains the key: answer with this status (and do nothing).</summary>
    public Dictionary<string, HttpStatusCode> FailNext { get; } = new();

    /// <summary>Next call whose path contains the key: perform it, then lose the response (connection reset).</summary>
    public HashSet<string> LoseResponseOfNext { get; } = [];

    /// <summary>Added to every response (to test timeouts).</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>Runs after a process instance has been created and before the response is returned.</summary>
    public Action<string>? AfterCreate { get; set; }

    public List<Call> Calls { get; } = [];

    public void Reset()
    {
        lock (_lock)
        {
            Down = false;
            Deployed = true;
            FailNext.Clear();
            LoseResponseOfNext.Clear();
            Delay = TimeSpan.Zero;
            AfterCreate = null;
            Calls.Clear();
        }
    }

    public Instance InstanceOf(string key)
    {
        lock (_lock) return _instances[key];
    }

    public Instance[] InstancesFor(string businessId)
    {
        lock (_lock) return _instances.Values.Where(i => i.BusinessId == businessId).OrderBy(i => i.Started).ToArray();
    }

    public Call[] CallsTo(string pathPart)
    {
        lock (_lock) return Calls.Where(c => c.Path.Contains(pathPart)).ToArray();
    }

    /// <summary>Delivers a message as if someone else (another system, Operate) had sent it.</summary>
    public bool Deliver(string messageName, string correlationKey)
    {
        lock (_lock) return Correlate(messageName, correlationKey) is not null;
    }

    // --- HTTP ---------------------------------------------------------------------------------------

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        lock (_lock) Calls.Add(new Call(request.Method.Method, path, body, request.Headers.Authorization?.ToString()));

        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        if (Down) throw new HttpRequestException("No connection could be made because the target machine actively refused it. (camunda.test:8080)");

        bool lose;
        lock (_lock)
        {
            var fail = FailNext.Keys.FirstOrDefault(path.Contains);
            if (fail is not null)
            {
                var status = FailNext[fail];
                FailNext.Remove(fail);
                return Problem(status, status.ToString().ToUpperInvariant(), $"Injected failure for {path}");
            }
            var loseKey = LoseResponseOfNext.FirstOrDefault(path.Contains);
            lose = loseKey is not null && LoseResponseOfNext.Remove(loseKey);
        }

        var response = Handle(request.Method.Method, path, body is { Length: > 0 } ? JsonNode.Parse(body) : null);
        if (lose) throw new HttpRequestException("The response ended prematurely. (connection reset)");
        return response;
    }

    private HttpResponseMessage Handle(string method, string path, JsonNode? body)
    {
        var p = path.StartsWith("/v2/") ? path[4..] : path.TrimStart('/');
        lock (_lock)
        {
            switch (method, p)
            {
                case ("GET", "topology"):
                    return Json(new JsonObject
                    {
                        ["brokers"] = new JsonArray(new JsonObject
                        {
                            ["nodeId"] = 0, ["host"] = "172.19.0.2", ["port"] = 26501, ["version"] = GatewayVersion,
                            ["partitions"] = new JsonArray(new JsonObject { ["partitionId"] = 1, ["role"] = "leader", ["health"] = "healthy" }),
                        }),
                        ["clusterId"] = "fake", ["clusterSize"] = 1, ["partitionsCount"] = 1, ["replicationFactor"] = 1,
                        ["gatewayVersion"] = GatewayVersion, ["lastCompletedChangeId"] = "-1",
                    });

                case ("POST", "process-definitions/search"):
                    var definitions = new JsonArray();
                    if (Deployed && FilterValue(body?["filter"]?["processDefinitionId"]) == _model.ProcessId)
                        definitions.Add(new JsonObject
                        {
                            ["name"] = "Modon Excavation Works Permit", ["resourceName"] = "excavation-permit.bpmn", ["version"] = 2,
                            ["versionTag"] = _model.VersionTag, ["processDefinitionId"] = _model.ProcessId, ["tenantId"] = "<default>",
                            ["processDefinitionKey"] = DefinitionKey, ["hasStartForm"] = false, ["state"] = "ACTIVE",
                        });
                    return Json(Page(definitions));

                case ("POST", "process-instances"):
                    return CreateInstance(body!);

                case ("POST", "messages/correlation"):
                    var name = (string)body!["name"]!;
                    var key = (string)body["correlationKey"]!;
                    var delivered = Correlate(name, key, body["variables"] as JsonObject);
                    return delivered is null
                        ? Problem(HttpStatusCode.NotFound, "NOT_FOUND",
                            $"Command 'CORRELATE' rejected with code 'NOT_FOUND': Expected to find subscription for message with name '{name}' and correlation key '{key}', but none was found.")
                        : Json(new JsonObject { ["tenantId"] = "<default>", ["messageKey"] = NewKey(), ["processInstanceKey"] = delivered.Key });

                case ("POST", "element-instances/search"):
                    var of = FilterValue(body?["filter"]?["processInstanceKey"]);
                    var items = new JsonArray();
                    if (of is not null && _instances.TryGetValue(of, out var inst))
                        foreach (var v in inst.Visits) items.Add(VisitJson(inst, v));
                    return Json(Page(items));
            }

            var parts = p.Split('/');
            if (parts is ["process-instances", var instanceKey] && method == "GET")
                return _instances.TryGetValue(instanceKey, out var found)
                    ? Json(InstanceJson(found))
                    : Problem(HttpStatusCode.NotFound, "NOT_FOUND", $"Process Instance with key '{instanceKey}' not found");
            if (parts is ["process-instances", var cancelKey, "cancellation"] && method == "POST")
            {
                if (!_instances.TryGetValue(cancelKey, out var victim) || victim.State != "ACTIVE")
                    return Problem(HttpStatusCode.NotFound, "NOT_FOUND",
                        $"Command 'CANCEL' rejected with code 'NOT_FOUND': Expected to cancel a process instance with key '{cancelKey}', but no such process was found");
                victim.State = "TERMINATED";
                if (victim.Waiting is { } w) w.End = DateTimeOffset.UtcNow;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }
        return Problem(HttpStatusCode.NotFound, "NOT_FOUND", $"Fake Camunda has no route for {method} {path}");
    }

    private HttpResponseMessage CreateInstance(JsonNode body)
    {
        var processId = (string?)body["processDefinitionId"];
        if (!Deployed || processId != _model.ProcessId)
            return Problem(HttpStatusCode.NotFound, "NOT_FOUND",
                $"Command 'CREATE' rejected with code 'NOT_FOUND': Expected to find process definition with process ID '{processId}', but none found");

        var instance = new Instance
        {
            Key = NewKey(),
            BusinessId = (string?)body["businessId"] ?? "",
            Variables = body["variables"]?.DeepClone() as JsonObject ?? new JsonObject(),
            Started = DateTimeOffset.UtcNow,
        };
        _instances[instance.Key] = instance;
        var now = DateTimeOffset.UtcNow;
        instance.Visits.Add(new Visit { Key = NewKey(), ElementId = _model.StartEventId, Type = "START_EVENT", Start = now, End = now });
        Run(instance, _model.StartEventId);
        AfterCreate?.Invoke(instance.Key);

        return Json(new JsonObject
        {
            ["processDefinitionId"] = _model.ProcessId, ["processDefinitionVersion"] = 2, ["tenantId"] = "<default>",
            ["variables"] = new JsonObject(), ["processDefinitionKey"] = DefinitionKey, ["processInstanceKey"] = instance.Key,
            ["tags"] = new JsonArray(), ["businessId"] = instance.BusinessId,
        });
    }

    /// <summary>Message correlation as Camunda does it: only to an instance currently waiting for that message.</summary>
    private Instance? Correlate(string name, string correlationKey, JsonObject? variables = null)
    {
        foreach (var instance in _instances.Values.Where(i => i.State == "ACTIVE" && i.CorrelationKey == correlationKey).OrderBy(i => i.Started))
        {
            var waiting = instance.Waiting!;
            if (!_model.AcceptedMessages(waiting.ElementId).TryGetValue(name, out var catchEvent)) continue;

            var now = DateTimeOffset.UtcNow;
            waiting.End = now;
            if (catchEvent.Id != waiting.ElementId)
                instance.Visits.Add(new Visit { Key = NewKey(), ElementId = catchEvent.Id, Type = catchEvent.Type, Start = now, End = now });
            foreach (var (k, v) in variables ?? []) instance.Variables[k] = v?.DeepClone();
            Run(instance, catchEvent.Id);
            return instance;
        }
        return null;
    }

    private void Run(Instance instance, string fromId)
    {
        var path = _model.RunFrom(fromId);
        foreach (var element in path)
        {
            var now = DateTimeOffset.UtcNow;
            var last = ReferenceEquals(element, path[^1]);
            var waits = last && element.Type != "END_EVENT";
            instance.Visits.Add(new Visit { Key = NewKey(), ElementId = element.Id, Type = element.Type, Start = now, End = waits ? null : now });
        }
        if (path[^1].Type == "END_EVENT") instance.State = "COMPLETED";
    }

    private string NewKey() => (_nextKey++).ToString();

    /// <summary>A search filter value, sent either plain or as an advanced filter <c>{"$eq": ...}</c>.</summary>
    private static string? FilterValue(JsonNode? filter) => filter switch
    {
        JsonValue v => v.ToString(),
        JsonObject o => o["$eq"]?.ToString(),
        _ => null,
    };

    private JsonObject InstanceJson(Instance i) => new()
    {
        ["processDefinitionId"] = _model.ProcessId, ["processDefinitionName"] = "Modon Excavation Works Permit",
        ["processDefinitionVersion"] = 2, ["processDefinitionVersionTag"] = _model.VersionTag,
        ["startDate"] = i.Started.ToString("O"),
        ["endDate"] = i.State == "ACTIVE" ? null : i.Visits.Last().End?.ToString("O"),
        ["state"] = i.State, ["hasIncident"] = false, ["tenantId"] = "<default>", ["processInstanceKey"] = i.Key,
        ["processDefinitionKey"] = DefinitionKey, ["parentProcessInstanceKey"] = null, ["parentElementInstanceKey"] = null,
        ["rootProcessInstanceKey"] = i.Key, ["tags"] = new JsonArray(), ["businessId"] = i.BusinessId,
    };

    private JsonObject VisitJson(Instance i, Visit v) => new()
    {
        ["processDefinitionId"] = _model.ProcessId, ["startDate"] = v.Start.ToString("O"), ["endDate"] = v.End?.ToString("O"),
        ["elementId"] = v.ElementId, ["elementName"] = v.ElementId, ["type"] = v.Type,
        ["state"] = i.State == "TERMINATED" && v.End is not null && v == i.Visits.Last() ? "TERMINATED" : v.State,
        ["hasIncident"] = false, ["tenantId"] = "<default>", ["elementInstanceKey"] = v.Key, ["processInstanceKey"] = i.Key,
        ["rootProcessInstanceKey"] = i.Key, ["processDefinitionKey"] = DefinitionKey, ["incidentKey"] = null,
    };

    private static JsonObject Page(JsonArray items) => new()
    {
        ["page"] = new JsonObject { ["totalItems"] = items.Count, ["hasMoreTotalItems"] = false, ["startCursor"] = null, ["endCursor"] = null },
        ["items"] = items,
    };

    private static HttpResponseMessage Json(JsonNode node) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Problem(HttpStatusCode status, string title, string detail) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            type = "about:blank", title, status = (int)status, detail, instance = "/v2",
        }), Encoding.UTF8, "application/problem+json"),
    };
}
