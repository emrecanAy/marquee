using Marquee.Common.Messaging;

namespace Marquee.Common.Contracts;

public sealed record PaymentSucceeded(Guid PaymentId, Guid ReservationId) : IIntegrationEvent;

public sealed record PaymentFailed(Guid PaymentId, Guid ReservationId, string Reason) : IIntegrationEvent;

/// <summary>
/// Published by Booking when money arrived for a reservation that no longer holds its seats.
/// Payments answers with a refund.
/// </summary>
public sealed record ReservationRejected(Guid ReservationId, Guid PaymentId, string Reason) : IIntegrationEvent;
