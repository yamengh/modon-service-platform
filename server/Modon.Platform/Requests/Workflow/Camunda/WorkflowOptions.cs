namespace Modon.Platform.Requests.Workflow.Camunda;

public enum WorkflowEngineKind
{
    /// <summary>Rules evaluated in the backend only (automated tests, local fallback).</summary>
    InProcess,

    /// <summary>The excavation BPMN running in a Camunda 8 orchestration cluster.</summary>
    Camunda,
}

/// <summary>Configuration section <c>Modon:Workflow</c>.</summary>
public sealed class WorkflowOptions
{
    public const string Section = "Modon:Workflow";

    public WorkflowEngineKind Engine { get; set; } = WorkflowEngineKind.InProcess;

    public CamundaWorkflowOptions Camunda { get; set; } = new();

    /// <summary>Throws a readable error for settings that cannot work (checked once at startup).</summary>
    public void Validate()
    {
        if (Engine != WorkflowEngineKind.Camunda) return;
        var c = Camunda;
        if (!Uri.TryCreate(c.RestAddress, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException(
                $"{Section}:Camunda:RestAddress must be the cluster's REST address, e.g. http://127.0.0.1:8080 (got '{c.RestAddress}').");
        if (c.AuthStrategy is not (CamundaWorkflowOptions.AuthNone or CamundaWorkflowOptions.AuthBasic))
            throw new InvalidOperationException(
                $"{Section}:Camunda:AuthStrategy must be {CamundaWorkflowOptions.AuthNone} or {CamundaWorkflowOptions.AuthBasic} (got '{c.AuthStrategy}').");
        if (c.AuthStrategy == CamundaWorkflowOptions.AuthBasic && (string.IsNullOrEmpty(c.BasicUsername) || string.IsNullOrEmpty(c.BasicPassword)))
            throw new InvalidOperationException($"{Section}:Camunda:BasicUsername and BasicPassword are required for BASIC authentication.");
        if (string.IsNullOrWhiteSpace(c.ProcessId))
            throw new InvalidOperationException($"{Section}:Camunda:ProcessId is required.");
        if (c.RequestTimeoutSeconds is < 1 or > 120)
            throw new InvalidOperationException($"{Section}:Camunda:RequestTimeoutSeconds must be between 1 and 120.");
    }
}

/// <summary>Configuration section <c>Modon:Workflow:Camunda</c>.</summary>
public sealed class CamundaWorkflowOptions
{
    public const string AuthNone = "NONE";
    public const string AuthBasic = "BASIC";

    /// <summary>Orchestration cluster REST address without <c>/v2</c>, e.g. <c>http://127.0.0.1:8080</c>.</summary>
    public string RestAddress { get; set; } = "";

    /// <summary><c>NONE</c> (cluster with an unprotected API) or <c>BASIC</c> (username / password).</summary>
    public string AuthStrategy { get; set; } = AuthNone;

    public string? BasicUsername { get; set; }
    public string? BasicPassword { get; set; }

    /// <summary>BPMN process id of the deployed excavation process.</summary>
    public string ProcessId { get; set; } = ExcavationProcess.DefaultProcessId;

    /// <summary>Tenant id; leave empty when multi-tenancy is off.</summary>
    public string? TenantId { get; set; }

    /// <summary>How long one call to Camunda may take before the action is refused as unavailable.</summary>
    public int RequestTimeoutSeconds { get; set; } = 10;
}
