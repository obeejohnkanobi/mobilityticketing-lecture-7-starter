
using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Commands;

/// <summary>
/// Commands that modify the authoritative source tables.
///
/// Cancellation changes the existing trip status.
/// Renaming changes the current route name.
///
/// Historical published snapshots must never be modified here.
/// </summary>
public static class TripCommands
{
    /// <summary>
    /// Cancels a trip without deleting its database row.
    /// Repeated cancellation returns AlreadyCancelled.
    /// </summary>
    public static Task<CancelResult> CancelAsync(
        string connection,
        string tripId,
        CancellationToken ct)
    {
        // Reject missing or whitespace-only IDs before opening
        // a database connection or starting a transaction.
        DatabaseSession.RequireText(tripId, nameof(tripId));

        return DatabaseSession.WriteAsync(
            connection,
            $"cancel:{tripId}",
            async db =>
            {
                // Acquire a PostgreSQL row-level lock (FOR UPDATE).
                //
                // Booking uses the same trip-row lock.
                // This prevents booking from reading an outdated
                // status while cancellation modifies that trip.
                var trip = await DatabaseSession.LockTripAsync(
                    db, tripId, ct);

                if (trip is null)
                    return CancelResult.NotFound;

                // Idempotent behavior: do not update an already
                // cancelled trip a second time.
                if (trip.Status == "Cancelled")
                    return CancelResult.AlreadyCancelled;

                // State transition: Scheduled -> Cancelled.
                // The trip row, departure time and route ID remain.
                trip.Status = "Cancelled";

                // WriteAsync handles SaveChangesAsync and COMMIT.
                return CancelResult.Cancelled;
            },
            ct);
    }

    /// <summary>
    /// Renames an existing route.
    /// The next live overview query sees the updated name.
    /// </summary>
    public static Task<RenameResult> RenameAsync(
        string connection,
        string routeId,
        string name,
        CancellationToken ct)
    {
        DatabaseSession.RequireText(routeId, nameof(routeId));
        DatabaseSession.RequireText(name, nameof(name));

        // Prevent leading and trailing whitespace from being stored.
        var trimmed = name.Trim();

        return DatabaseSession.WriteAsync(
            connection,
            $"route:{routeId}",
            async db =>
            {
                // Find the tracked entity.
                var route = await db.Routes
                    .SingleOrDefaultAsync(
                        r => r.Id == routeId, ct);

                if (route is null)
                    return RenameResult.NotFound;

                // EF Core change tracking detects this update.
                route.Name = trimmed;

                return RenameResult.Renamed;
            },
            ct);
    }
}
