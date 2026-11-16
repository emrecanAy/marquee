using System.Net.Http.Json;
using Marquee.Booking.Reservations;
using Marquee.Booking.Seats;
using Marquee.Payments.Features;

namespace Marquee.Tests.Support;

/// <summary>The HTTP calls the web client makes, so tests read like a visitor's actions.</summary>
internal static class Api
{
    public static Task<HttpResponseMessage> HoldAsync(this HttpClient visitor, Guid showtimeId, params string[] seats) =>
        visitor.PostAsJsonAsync($"/api/showtimes/{showtimeId}/reservations", new { seats });

    public static async Task<ReservationResponse> HoldSuccessfullyAsync(
        this HttpClient visitor, Guid showtimeId, params string[] seats)
    {
        var response = await visitor.HoldAsync(showtimeId, seats);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReservationResponse>())!;
    }

    public static async Task<ReservationResponse> GetReservationAsync(this HttpClient visitor, Guid reservationId) =>
        (await visitor.GetFromJsonAsync<ReservationResponse>($"/api/reservations/{reservationId}"))!;

    public static async Task<Dictionary<string, string>> GetSeatStatusesAsync(this HttpClient visitor, Guid showtimeId)
    {
        var seats = await visitor.GetFromJsonAsync<List<GetSeatMap.SeatResponse>>($"/api/showtimes/{showtimeId}/seats");
        return seats!.ToDictionary(seat => seat.Label, seat => seat.Status);
    }

    public static async Task<HttpResponseMessage> PayAsync(
        this HttpClient visitor, Guid reservationId, string? idempotencyKey, string paymentToken = "tok_approved")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/reservations/{reservationId}/payments")
        {
            Content = JsonContent.Create(new { paymentToken }),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await visitor.SendAsync(request);
    }

    public static async Task<PaymentResponse> PaySuccessfullyAsync(
        this HttpClient visitor, Guid reservationId, string idempotencyKey, string paymentToken = "tok_approved")
    {
        var response = await visitor.PayAsync(reservationId, idempotencyKey, paymentToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PaymentResponse>())!;
    }

    public static async Task<PaymentResponse> GetPaymentAsync(this HttpClient visitor, Guid paymentId) =>
        (await visitor.GetFromJsonAsync<PaymentResponse>($"/api/payments/{paymentId}"))!;

    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
        return problem!.TryGetValue("code", out var code) ? code?.ToString() : null;
    }
}
