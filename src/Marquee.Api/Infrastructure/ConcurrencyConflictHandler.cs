using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Api.Infrastructure;

/// <summary>
/// An xmin version check that no handler dealt with explicitly: somebody changed the same row
/// first. That is the client's cue to reload, not a server fault.
/// </summary>
internal sealed class ConcurrencyConflictHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status409Conflict,
                Detail = "The data changed while the request was being processed. Reload and try again.",
                Extensions = { ["code"] = "concurrent_update" },
            },
        });
    }
}
