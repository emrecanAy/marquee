using Marquee.Booking.Admission;
using Marquee.Booking.Persistence;
using Marquee.Booking.Reservations;
using Marquee.Booking.Screenings;
using Marquee.Booking.Seats;
using Marquee.Common.Contracts;
using Marquee.Common.Messaging;
using Marquee.Common.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marquee.Booking;

public static class BookingModule
{
    public static IServiceCollection AddBookingModule(this IServiceCollection services)
    {
        services.AddOptions<BookingOptions>().BindConfiguration("Booking");
        services.AddOptions<WaitingRoomOptions>().BindConfiguration("WaitingRoom");

        services.AddModuleDbContext<BookingDbContext>(BookingDbContext.Schema);
        services.AddOutbox<BookingDbContext>();

        services.AddSingleton<SeatLocks>();
        services.AddSingleton<WaitingRoom>();
        services.AddScoped<SeatRelease>();
        services.AddScoped<HoldSeats.Handler>();
        services.AddScoped<ReservationExpiry>();
        services.AddScoped<IReservationCheckout, ReservationCheckout>();

        services.AddIntegrationEventHandler<ShowtimeScheduled, ShowtimeScheduledHandler>();
        services.AddIntegrationEventHandler<PaymentSucceeded, PaymentSucceededHandler>();

        services.AddHostedService<ReservationExpiryWorker>();
        services.AddHostedService<WaitingRoomDispatcher>();
        return services;
    }

    public static void MapBookingEndpoints(this IEndpointRouteBuilder api)
    {
        WaitingRoomEndpoints.Map(api);
        GetSeatMap.Map(api);
        HoldSeats.Map(api);
        ReservationEndpoints.Map(api);
    }

    public static Task MigrateBookingAsync(this IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<BookingDbContext>().Database.MigrateAsync(ct);
}
