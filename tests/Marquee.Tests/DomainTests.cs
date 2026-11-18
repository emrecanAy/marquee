using Marquee.Booking.Domain;
using Marquee.Catalog.Domain;
using Marquee.Common.Web;

namespace Marquee.Tests;

public sealed class DomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan HoldFor = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    [Fact]
    public void A_hall_prices_its_back_rows_as_premium()
    {
        var hall = new Hall { Name = "Test", RowCount = 3, SeatsPerRow = 2, PremiumRowCount = 1 };

        var seats = hall.PriceSeats(basePrice: 100m);

        Assert.Equal(new[] { "A1", "A2", "B1", "B2", "C1", "C2" }, seats.Select(seat => seat.Label));
        Assert.All(seats.Where(seat => seat.Row != "C"), seat => Assert.Equal((Hall.StandardCategory, 100m), (seat.Category, seat.Price)));
        Assert.All(seats.Where(seat => seat.Row == "C"), seat => Assert.Equal((Hall.PremiumCategory, 140m), (seat.Category, seat.Price)));
    }

    [Fact]
    public void A_seat_that_is_held_cannot_be_held_again()
    {
        var seat = NewSeat("A1");
        Assert.True(seat.Hold(Guid.NewGuid()).IsSuccess);

        var second = seat.Hold(Guid.NewGuid());

        Assert.Equal("seat_taken", second.Error?.Code);
    }

    [Fact]
    public void A_reservation_totals_its_seats_and_expires_after_the_hold_duration()
    {
        var reservation = Reservation.Hold(
            Guid.NewGuid(), Guid.NewGuid(), new TicketOwner(Guid.NewGuid(), null), [NewSeat("A2", 100m), NewSeat("A1", 140m)], Now, HoldFor);

        Assert.Equal(240m, reservation.Amount);
        Assert.Equal(new[] { "A1", "A2" }, reservation.SeatLabels);
        Assert.Equal(Now + HoldFor, reservation.ExpiresAt);
    }

    [Fact]
    public void Submitting_payment_late_in_the_hold_stretches_it_by_the_grace_period()
    {
        var reservation = NewReservation();
        var lastMinute = Now + HoldFor - TimeSpan.FromSeconds(10);

        Assert.True(reservation.BeginPayment(lastMinute, Grace, HoldFor + Grace).IsSuccess);

        Assert.Equal(lastMinute + Grace, reservation.ExpiresAt);
    }

    [Fact]
    public void Repeated_payment_attempts_cannot_stretch_a_hold_forever()
    {
        var reservation = NewReservation();
        var attempt = Now + HoldFor - TimeSpan.FromSeconds(10);

        for (var i = 0; i < 5 && reservation.BeginPayment(attempt, Grace, HoldFor + Grace).IsSuccess; i++)
        {
            attempt = reservation.ExpiresAt - TimeSpan.FromSeconds(1);
        }

        Assert.Equal(Now + HoldFor + Grace, reservation.ExpiresAt);
    }

    [Fact]
    public void Payment_cannot_begin_once_the_hold_has_run_out()
    {
        var reservation = NewReservation();

        var result = reservation.BeginPayment(Now + HoldFor, Grace, HoldFor + Grace);

        Assert.Equal("reservation_expired", result.Error?.Code);
    }

    [Fact]
    public void A_closed_reservation_cannot_be_confirmed()
    {
        var reservation = NewReservation();
        reservation.Expire();

        var result = reservation.Confirm();

        Assert.Equal("reservation_not_pending", result.Error?.Code);
        Assert.Null(reservation.TicketCode);
    }

    private static Reservation NewReservation() =>
        Reservation.Hold(Guid.NewGuid(), Guid.NewGuid(), new TicketOwner(Guid.NewGuid(), null), [NewSeat("A1")], Now, HoldFor);

    private static Seat NewSeat(string label, decimal price = 100m) => new()
    {
        ScreeningId = Guid.NewGuid(), Label = label, Row = label[..1], Number = int.Parse(label[1..]),
        Category = "standard", Price = price,
    };
}
