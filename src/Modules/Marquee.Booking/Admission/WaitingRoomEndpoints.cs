using System.Security.Claims;
using Marquee.Common.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Marquee.Booking.Admission;

internal static class WaitingRoomEndpoints
{
    public sealed record QueueResponse(string State, int Position, DateTimeOffset? AdmittedUntil)
    {
        public static QueueResponse From(QueueTicket ticket) =>
            new(ticket.State.ToString().ToLowerInvariant(), ticket.Position, ticket.AdmittedUntil);
    }

    public static void Map(IEndpointRouteBuilder api)
    {
        var queue = api.MapGroup("/showtimes/{showtimeId:guid}/queue").RequireAuthorization().WithTags("Waiting room");

        queue.MapPost("", async (Guid showtimeId, ClaimsPrincipal user, WaitingRoom waitingRoom) =>
            QueueResponse.From(await waitingRoom.JoinAsync(showtimeId, user.GetSessionId())));

        queue.MapGet("", async (Guid showtimeId, ClaimsPrincipal user, WaitingRoom waitingRoom) =>
            QueueResponse.From(await waitingRoom.GetAsync(showtimeId, user.GetSessionId())));

        queue.MapDelete("", async (Guid showtimeId, ClaimsPrincipal user, WaitingRoom waitingRoom) =>
        {
            await waitingRoom.LeaveAsync(showtimeId, user.GetSessionId());
            return Results.NoContent();
        });
    }
}
