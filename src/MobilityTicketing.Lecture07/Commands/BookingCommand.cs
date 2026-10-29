
using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Commands;

/// <summary>
/// Handles idempotent trip booking.
///
/// Two different concurrency problems are addressed:
///
/// 1. Concurrent retries using the same request ID.
/// 2. Concurrent booking and cancellation of the same trip.
///
/// Advisory locks serialize request IDs.
/// Row-level locks protect the authoritative trip status.
/// </summary>
public static class BookingCommand
{
    public static Task<BookingResult> ExecuteAsync(
        string connection,
        string tripId,
        string requestId,
        TimeProvider clock,
        CancellationToken ct)
    {
        DatabaseSession.RequireText(tripId, nameof(tripId));
        DatabaseSession.RequireText(requestId, nameof(requestId));

        // The transaction-scoped advisory lock ensures that
        // concurrent operations using the same request ID
        // do not create duplicate bookings.
        return DatabaseSession.WriteAsync(
            connection,
            $"booking:{requestId}",
            async db =>
            {
                // IMPORTANT: Check for an existing receipt first.
                //
                // A previously successful booking must remain
                // retrievable even when its trip is later cancelled.
                var existing = await db.Bookings
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        b => b.RequestId == requestId, ct);

                if (existing is not null)
                {
                    // A request ID cannot represent two trips.
                    if (existing.TripId != tripId)
                    {
                        return new BookingResult(
                            BookingStatus.RequestConflict);
                    }

                    // Return the original persisted receipt.
                    // Never replace its timestamp on retry.
                    return new BookingResult(
                        BookingStatus.AlreadyBooked,
                        new BookingReceipt(
                            existing.RequestId,
                            existing.TripId,
                            existing.BookedAtUtc));
                }

                // Lock the trip before authorizing a NEW booking.
                // Cancellation acquires the same row-level lock.
                var trip = await DatabaseSession.LockTripAsync(
                    db, tripId, ct);

                if (trip is null)
                {
                    return new BookingResult(
                        BookingStatus.NotFound);
                }

                // The current database state is authoritative.
                // Never trust an old departure-board screen.
                if (trip.Status != "Scheduled")
                {
                    return new BookingResult(
                        BookingStatus.Cancelled);
                }

                // PostgreSQL stores timestamps at microsecond
                // precision, while DateTime supports 100ns ticks.
                //
                // Normalize the receipt timestamp BEFORE storage
                // so the initial response and later retries match.
                var now = clock.GetUtcNow().UtcDateTime;

                var timestamp = new DateTime(
                    now.Ticks -
                    now.Ticks % TimeSpan.TicksPerMicrosecond,
                    DateTimeKind.Utc);

                // EF Core tracks the new entity and INSERTs it
                // when DatabaseSession calls SaveChangesAsync.
                db.Bookings.Add(new Booking
                {
                    RequestId = requestId,
                    TripId = tripId,
                    BookedAtUtc = timestamp
                });

                return new BookingResult(
                    BookingStatus.Booked,
                    new BookingReceipt(
                        requestId,
                        tripId,
                        timestamp));
            },
            ct);
    }
}
