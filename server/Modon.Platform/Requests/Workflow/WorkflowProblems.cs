using Microsoft.EntityFrameworkCore;

namespace Modon.Platform.Requests.Workflow;

/// <summary>Turns workflow and concurrency failures of a status change into the API's problem responses.</summary>
public static class WorkflowProblems
{
    /// <param name="refusedCode">Problem title when the step is not possible now (409).</param>
    /// <param name="changedCode">Problem title when someone else changed the request first (409).</param>
    /// <returns>The response, or null when <paramref name="e"/> is not a workflow failure.</returns>
    public static IResult? From(Exception e, string refusedCode, string changedCode) => e switch
    {
        InvalidTransitionException => Problem(409, refusedCode),
        DbUpdateConcurrencyException => Problem(409, changedCode),
        WorkflowNotStartedException => Problem(409, ProblemCodes.WorkflowNotStarted),
        // Details (addresses, engine messages) are logged and shown in reviewer diagnostics, not to every caller.
        WorkflowUnavailableException => Results.Problem(
            title: ProblemCodes.WorkflowUnavailable,
            detail: "The workflow engine is not available. Nothing was changed; please try again later.",
            statusCode: 503),
        _ => null,
    };

    private static IResult Problem(int status, string code) => Results.Problem(title: code, statusCode: status);
}
