using System.Security.Claims;
using Marquee.Common.Contracts;
using Marquee.Common.Messaging;
using Marquee.Common.Persistence;
using Marquee.Common.Results;
using Marquee.Common.Web;
using Marquee.Payments.Domain;
using Marquee.Payments.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Payments.Features;

internal static class PayReservation
{
    /// <param name="PaymentToken">
    /// A token from the provider's client-side SDK. Card numbers never reach this API.
    /// </param>
    public sealed record Request(string? PaymentToken);

    /// <summary>Asks the payment worker to charge the customer. Internal to this module.</summary>
    public sealed record ChargeRequested(Guid PaymentId, string PaymentToken) : IIntegrationEvent;

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/reservations/{reservationId:guid}/payments", async (
                Guid reservationId,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                Request request,
                ClaimsPrincipal user,
                Handler handler,
                CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(
                    reservationId, user.GetTicketOwner(), idempotencyKey, request.PaymentToken, ct);
                // 202: the payment is recorded and will be charged; the outcome is read from GET /payments/{id}.
                return result.Match(
                    payment => Results.Accepted($"/api/payments/{payment.Id}", payment),
                    error => error.ToProblem());
            })
            .RequireAuthorization()
            .WithTags("Payments");

    internal sealed class Handler(PaymentsDbContext db, IReservationCheckout checkout, TimeProvider time)
    {
        public async Task<Result<PaymentResponse>> HandleAsync(
            Guid reservationId, TicketOwner owner, string? idempotencyKey, string? paymentToken, CancellationToken ct)
        {
            // Payments belong to the browser session that started them; the reservation may also be the account's.
            var sessionId = owner.SessionId;
            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            {
                return Error.Validation("idempotency_key_required", "Send an Idempotency-Key header of at most 100 characters.");
            }

            if (string.IsNullOrWhiteSpace(paymentToken))
            {
                return Error.Validation("payment_token_required", "A payment token is required.");
            }

            var replay = await FindByKeyAsync(sessionId, idempotencyKey, ct);
            if (replay is not null)
            {
                return Replay(replay, reservationId);
            }

            var quote = await checkout.BeginPaymentAsync(reservationId, owner, ct);
            if (!quote.IsSuccess)
            {
                return quote.Error;
            }

            // The payment and the instruction to charge it are stored together. A crash after this
            // commit cannot lose the charge, and a crash before it leaves nothing behind.
            var payment = Payment.Start(reservationId, sessionId, idempotencyKey, quote.Value.Amount, time.GetUtcNow());
            db.Payments.Add(payment);
            db.Publish(new ChargeRequested(payment.Id, paymentToken));

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                // Lost a race against a concurrent request: either the same key (a retry that overlapped
                // the original) or another payment for the same reservation.
                db.ChangeTracker.Clear();
                var winner = await FindByKeyAsync(sessionId, idempotencyKey, ct);
                return winner is not null
                    ? Replay(winner, reservationId)
                    : Error.Conflict("payment_in_progress", "This reservation is already being paid for.");
            }

            return PaymentResponse.From(payment);
        }

        private Task<Payment?> FindByKeyAsync(Guid sessionId, string idempotencyKey, CancellationToken ct) =>
            db.Payments.AsNoTracking().SingleOrDefaultAsync(
                payment => payment.SessionId == sessionId && payment.IdempotencyKey == idempotencyKey, ct);

        private static Result<PaymentResponse> Replay(Payment existing, Guid reservationId) =>
            existing.ReservationId == reservationId
                ? PaymentResponse.From(existing)
                : Error.Validation("idempotency_key_reused", "This Idempotency-Key was used for another reservation.");
    }
}
