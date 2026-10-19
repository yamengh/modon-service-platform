using Microsoft.Extensions.Diagnostics.HealthChecks;
using Modon.Platform.Requests.Workflow.Camunda;

namespace Modon.Platform.Requests.Workflow;

/// <param name="Ready">True only when the engine can take actions now (for Camunda: reachable, healthy, process deployed).</param>
public sealed record WorkflowStatusReport(string Engine, bool Ready, string Summary, CamundaClusterReport? Camunda);

/// <summary>A request's status in PostgreSQL next to where its Camunda process instance is.</summary>
/// <param name="InSync">Null when it cannot be compared (no instance, or Camunda unreachable).</param>
public sealed record RequestWorkflowReport(
    string Engine,
    string RequestNumber,
    RequestStatus DatabaseStatus,
    string? ProcessInstanceKey,
    int? ProcessDefinitionVersion,
    string? EngineState,
    string? EngineElementId,
    RequestStatus? EngineStatus,
    bool? InSync,
    string? Note);

/// <summary>
/// Reports what the workflow engine really is and whether it works — the app starting is not proof that
/// Camunda is active. Used by the reviewer diagnostics endpoints, the health check and the startup log.
/// </summary>
public sealed class WorkflowDiagnostics(IRequestWorkflow workflow, IServiceProvider services)
{
    public async Task<WorkflowStatusReport> StatusAsync(CancellationToken ct)
    {
        if (services.GetService<ICamundaEngine>() is not { } engine || workflow is not CamundaRequestWorkflow)
            return new WorkflowStatusReport(workflow.EngineName, true,
                "In-process workflow engine: Camunda is disabled (Modon:Workflow:Engine = InProcess).", null);

        var c = await engine.DescribeAsync(ct);
        var ready = c is { Reachable: true, PartitionsHealthy: true, ProcessDeployed: true, Error: null };
        string summary;
        if (!c.Reachable) summary = $"Camunda is NOT reachable: {c.Error}";
        else if (!c.PartitionsHealthy) summary = $"Camunda {c.GatewayVersion} at {c.RestAddress} is reachable but its partitions are not healthy.";
        else if (c.Error is not null) summary = $"Camunda {c.GatewayVersion} at {c.RestAddress}: {c.Error}";
        else
        {
            summary = $"Camunda {c.GatewayVersion} at {c.RestAddress}: process '{c.ProcessId}' version {c.DeployedVersion} is deployed.";
            if (c.DeployedVersionTag != c.CheckedInVersionTag)
                summary += $" Note: deployed version tag '{c.DeployedVersionTag}' differs from the checked-in BPMN ('{c.CheckedInVersionTag}').";
        }
        return new WorkflowStatusReport(workflow.EngineName, ready, summary, c);
    }

    public async Task<RequestWorkflowReport> CheckRequestAsync(ServiceRequest r, CancellationToken ct)
    {
        RequestWorkflowReport Report(string? state, string? element, RequestStatus? status, bool? inSync, string? note) => new(
            workflow.EngineName, r.RequestNumber, r.Status, r.WorkflowInstanceKey, r.WorkflowDefinitionVersion,
            state, element, status, inSync, note);

        if (r.WorkflowInstanceKey is null)
            return Report(null, null, null, null, r.Status == workflow.InitialStatus
                ? "Draft: the process starts when the request is submitted."
                : "This request is not linked to a Camunda process instance (it was submitted with the in-process engine).");
        if (services.GetService<ICamundaEngine>() is not { } engine)
            return Report(null, null, null, null, "Camunda is disabled, so the linked process instance is not checked.");

        try
        {
            var view = await engine.GetInstanceAsync(r.WorkflowInstanceKey, ct);
            var note = view.State switch
            {
                "NOT_FOUND" => "Camunda has no process instance with this key (was the Camunda data reset?).",
                "TERMINATED" => "The process instance was cancelled in Camunda.",
                _ when view.Status is null => "Camunda's search data has not caught up yet; try again in a moment.",
                _ when view.HasIncident => "The process instance has an incident in Camunda (see Operate).",
                _ => null,
            };
            return Report(view.State, view.ElementId, view.Status,
                view.Status is null ? null : view.Status == r.Status, note);
        }
        catch (WorkflowUnavailableException e)
        {
            return Report(null, null, null, null, e.Message);
        }
    }
}

/// <summary><c>GET /health</c>: Healthy only when the configured workflow engine can take actions.</summary>
public sealed class WorkflowHealthCheck(WorkflowDiagnostics diagnostics) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var report = await diagnostics.StatusAsync(ct);
        return report.Ready ? HealthCheckResult.Healthy(report.Summary) : HealthCheckResult.Unhealthy(report.Summary);
    }
}

/// <summary>Logs, once after startup, which workflow engine is configured and whether it actually works.</summary>
public sealed class WorkflowStartupCheck(WorkflowDiagnostics diagnostics, ILogger<WorkflowStartupCheck> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // never delay startup
        try
        {
            var report = await diagnostics.StatusAsync(stoppingToken);
            if (report.Ready) logger.LogInformation("Workflow engine ready ({Engine}): {Summary}", report.Engine, report.Summary);
            else logger.LogError("Workflow engine NOT ready ({Engine}): {Summary} Submitting and reviewing requests will fail with 503 until this is fixed.",
                report.Engine, report.Summary);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }
}
