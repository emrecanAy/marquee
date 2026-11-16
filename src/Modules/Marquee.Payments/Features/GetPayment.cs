using System.Security.Claims;
using Marquee.Common.Results;
using Marquee.Common.Web;
using Marquee.Payments.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Payments.Features;

internal static class GetPayment
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/payments/{paymentId:guid}", async (
                Guid paymentId, ClaimsPrincipal user, PaymentsDbContext db, CancellationToken ct) =>
            {
                var sessionId = user.GetSessionId();
                var payment = await db.Payments.AsNoTracking()
                    .SingleOrDefaultAsync(p => p.Id == paymentId && p.SessionId == sessionId, ct);

                return payment is null
                    ? Error.NotFound("payment_not_found", "This payment does not exist.").ToProblem()
                    : Results.Ok(PaymentResponse.From(payment));
            })
            .RequireAuthorization()
            .WithTags("Payments");
}
