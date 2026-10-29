
using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Commands;

/// <summary>
/// Publishes a persistent snapshot of the route overview.
///
/// Publication is atomic:
/// either the plan and every snapshot row are saved,
/// or the entire transaction is rolled back.
///
/// Retrying an existing ID with the same window does not
/// overwrite the original historical values.
/// </summary>
public static class PublishPlanCommand
{
    public static Task<PublishResult> ExecuteAsync(
        string connection,
        string snapshotId,
        TimeWindow window,
        TimeProvider clock,
        ApplicationHooks hooks,
        CancellationToken ct)
    {
        DatabaseSession.RequireText(
            snapshotId, nameof(snapshotId));

        ArgumentNullException.ThrowIfNull(window);

        // Reject invalid UTC windows and submicrosecond
        // precision BEFORE accessing the database.
        window.Validate();

        return DatabaseSession.WriteAsync(
            connection,
            $"publish:{snapshotId}",
            async db =>
            {
                // The advisory lock serializes publication
                // attempts using the same snapshot ID.
                var previous = await db.Set<PublishedPlan>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        p => p.Id == snapshotId, ct);

                if (previous is not null)
                {
                    // Reusing an ID for another time window
                    // is a conflict, not a new publication.
                    if (previous.FromUtc != window.FromUtc ||
                        previous.ToUtc != window.ToUtc)
                    {
                        throw new SnapshotConflictException(
                            snapshotId);
                    }

                    // Identical retry: preserve the saved snapshot.
                    return PublishResult.AlreadyPublished;
                }

                // Calculate current values using SQL projection.
                // No complete Route or Trip entities are loaded.
                //
                // PostgreSQL evaluates this as one statement,
                // providing a consistent view for the query.
                var rows = await db.Routes
                    .AsNoTracking()
                    .OrderBy(route => route.Id)
                    .Select(route => new RouteOverviewDto(
                        route.Id,
                        route.Name,

                        // Count scheduled trips in the window.
                        route.Trips.Count(trip =>
                            trip.Status == "Scheduled" &&
                            trip.DepartureUtc >= window.FromUtc &&
                            trip.DepartureUtc < window.ToUtc),

                        // Find the earliest scheduled departure.
                        route.Trips
                            .Where(trip =>
                                trip.Status == "Scheduled" &&
                                trip.DepartureUtc >= window.FromUtc &&
                                trip.DepartureUtc < window.ToUtc)
                            .Select(trip =>
                                (DateTime?)trip.DepartureUtc)
                            .Min()))
                    .ToListAsync(ct);

                // Normalize the publication timestamp to the
                // precision supported by PostgreSQL.
                var now = clock.GetUtcNow().UtcDateTime;

                var timestamp = new DateTime(
                    now.Ticks -
                    now.Ticks % TimeSpan.TicksPerMicrosecond,
                    DateTimeKind.Utc);

                // Create the snapshot header.
                var plan = new PublishedPlan
                {
                    Id = snapshotId,
                    FromUtc = window.FromUtc,
                    ToUtc = window.ToUtc,
                    PublishedAtUtc = timestamp,

                    // Copy displayed values into historical rows.
                    // Later changes to Routes/Trips must not
                    // alter this saved data.
                    Rows = rows.Select(r => new PublishedPlanRow
                    {
                        PlanId = snapshotId,
                        RouteId = r.RouteId,
                        RouteName = r.RouteName,
                        ScheduledCount = r.ScheduledTripCount,
                        NextDepartureUtc = r.NextDepartureUtc
                    }).ToList()
                };

                // EF Core tracks the header and its child rows.
                db.Set<PublishedPlan>().Add(plan);

                // DatabaseSession saves and commits everything.
                // If saving or the failure hook throws,
                // the transaction is rolled back.
                return PublishResult.Published;
            },
            ct,
            beforeCommit: hooks.BeforePublishCommit);
    }
}
