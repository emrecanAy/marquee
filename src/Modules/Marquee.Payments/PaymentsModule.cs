using Marquee.Common.Contracts;
using Marquee.Common.Messaging;
using Marquee.Common.Persistence;
using Marquee.Payments.Features;
using Marquee.Payments.Gateway;
using Marquee.Payments.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marquee.Payments;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<PaymentsDbContext>(PaymentsDbContext.Schema);
        services.AddOutbox<PaymentsDbContext>();

        services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        services.AddScoped<PayReservation.Handler>();

        services.AddIntegrationEventHandler<PayReservation.ChargeRequested, ChargeRequestedHandler>();
        services.AddIntegrationEventHandler<ReservationRejected, ReservationRejectedHandler>();
        return services;
    }

    public static void MapPaymentsEndpoints(this IEndpointRouteBuilder api)
    {
        PayReservation.Map(api);
        GetPayment.Map(api);
    }

    public static Task MigratePaymentsAsync(this IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync(ct);
}
