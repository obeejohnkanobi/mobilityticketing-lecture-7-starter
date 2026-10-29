using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Queries;

/// <summary>
/// Reads the current route overview using EF Core projection.
/// Aggregations are executed in PostgreSQL rather than in memory.
/// </summary>
public static class ReadRouteOverviewQuery
{
    public static async Task<List<RouteOverviewDto>> ExecuteAsync(
        LectureDbContext db,
        TimeWindow window,
        CancellationToken ct = default)
    {
        // Validate UTC boundaries and microsecond precision
        // before executing any database query.
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(window);
        window.Validate();

        // AsNoTracking avoids unnecessary change tracking.
        // OrderBy ensures deterministic result ordering.
        return await db.Routes
            .AsNoTracking()
            .OrderBy(route => route.Id)
            .Select(route => new RouteOverviewDto(
                route.Id,
                route.Name,

                // Count scheduled trips in the requested window.
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
                    .Select(trip => (DateTime?)trip.DepartureUtc)
                    .Min()
            ))
            .ToListAsync(ct);
    }
}