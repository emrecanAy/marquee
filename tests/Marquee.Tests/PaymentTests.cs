using System.Net;
using Marquee.Payments.Persistence;
using Marquee.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marquee.Tests;

[Collection(AppCollection.Name)]
public sealed class PaymentTests(MarqueeApp app)
{
    [Fact]
    public async Task A_successful_payment_turns_the_hold_into_sold_seats_and_a_ticket()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var visitor = await app.CreateAdmittedVisitorAsync(showtimeId);
        var reservation = await visitor.HoldSuccessfullyAsync(showtimeId, "A1", "A2");

        var payment = await visitor.PaySuccessfullyAsync(reservation.Id, idempotencyKey: "pay-1");

        // Payment -> outbox -> charge -> outbox -> Booking: the confirmation arrives asynchronously.
        var confirmed = await Eventually.ValueAsync(
            () => visitor.GetReservationAsync(reservation.Id), r => r.Status == "confirmed");
        Assert.StartsWith("MQ-", confirmed.TicketCode);
        Assert.Equal("succeeded", (await visitor.GetPaymentAsync(payment.Id)).Status);

        var seats = await visitor.GetSeatStatusesAsync(showtimeId);
        Assert.Equal("sold", seats["A1"]);
        Assert.Equal("sold", seats["A2"]);
    }

    [Fact]
    public async Task Retrying_with_the_same_idempotency_key_does_not_create_a_second_payment()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var visitor = await app.CreateAdmittedVisitorAsync(showtimeId);
        var reservation = await visitor.HoldSuccessfullyAsync(showtimeId, "A1");

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => visitor.PaySuccessfullyAsync(reservation.Id, idempotencyKey: "pay-once")));

        Assert.Single(attempts.Select(payment => payment.Id).Distinct());
        await using var scope = app.Services.CreateAsyncScope();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Payments;
        Assert.Equal(1, await payments.CountAsync(payment => payment.ReservationId == reservation.Id));
    }

    [Fact]
    public async Task A_second_payment_with_a_different_key_is_refused_while_the_first_is_in_flight()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var visitor = await app.CreateAdmittedVisitorAsync(showtimeId);
        var reservation = await visitor.HoldSuccessfullyAsync(showtimeId, "A1");
        await visitor.PaySuccessfullyAsync(reservation.Id, idempotencyKey: "tab-1", paymentToken: "tok_slow");

        var response = await visitor.PayAsync(reservation.Id, idempotencyKey: "tab-2");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("payment_in_progress", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task A_declined_card_keeps_the_hold_so_the_customer_can_try_again()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var visitor = await app.CreateAdmittedVisitorAsync(showtimeId);
        var reservation = await visitor.HoldSuccessfullyAsync(showtimeId, "A1");

        var declined = await visitor.PaySuccessfullyAsync(reservation.Id, "attempt-1", paymentToken: "tok_declined");
        var failed = await Eventually.ValueAsync(() => visitor.GetPaymentAsync(declined.Id), p => p.Status == "failed");
        Assert.Equal("card_declined", failed.FailureReason);
        Assert.Equal("pending", (await visitor.GetReservationAsync(reservation.Id)).Status);

        await visitor.PaySuccessfullyAsync(reservation.Id, "attempt-2");
        await Eventually.ValueAsync(() => visitor.GetReservationAsync(reservation.Id), r => r.Status == "confirmed");
    }

    /// <summary>
    /// The compensation path: the provider takes the money, but by the time the answer arrives the
    /// hold is gone. Booking rejects the confirmation and Payments refunds.
    /// </summary>
    [Fact]
    public async Task A_charge_that_lands_after_the_hold_expired_is_refunded()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var visitor = await app.CreateAdmittedVisitorAsync(showtimeId);
        var reservation = await visitor.HoldSuccessfullyAsync(showtimeId, "A1");

        var payment = await visitor.PaySuccessfullyAsync(reservation.Id, "late", paymentToken: "tok_slow");
        await app.ExpireHoldsAsync();

        var refunded = await Eventually.ValueAsync(() => visitor.GetPaymentAsync(payment.Id), p => p.Status == "refunded");
        Assert.Equal("reservation_no_longer_held", refunded.FailureReason);
        Assert.Equal("expired", (await visitor.GetReservationAsync(reservation.Id)).Status);
    }

    [Fact]
    public async Task Paying_requires_an_idempotency_key()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var visitor = await app.CreateAdmittedVisitorAsync(showtimeId);
        var reservation = await visitor.HoldSuccessfullyAsync(showtimeId, "A1");

        var response = await visitor.PayAsync(reservation.Id, idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_required", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task One_visitor_cannot_pay_for_or_see_another_visitors_reservation()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var owner = await app.CreateAdmittedVisitorAsync(showtimeId);
        var stranger = await app.CreateAdmittedVisitorAsync(showtimeId);
        var reservation = await owner.HoldSuccessfullyAsync(showtimeId, "A1");

        var pay = await stranger.PayAsync(reservation.Id, idempotencyKey: "not-mine");
        var read = await stranger.GetAsync($"/api/reservations/{reservation.Id}");

        Assert.Equal(HttpStatusCode.NotFound, pay.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }
}
