
using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Queries;

/// <summary>
/// Reads a previously published route plan.
///
/// Uses one SQL query against the snapshot tables.
/// Does not modify data or load current Route/Trip entities.
/// </summary>
public static class ReadPublishedPlanQuery
{
    public static async Task<PublishedSnapshot> ExecuteAsync(
        LectureDbContext db,
        string snapshotId,
        CancellationToken ct)
    {
        DatabaseSession.RequireText(
            snapshotId, nameof(snapshotId));

        // Use LEFT JOIN semantics to preserve the plan
        // header even if it has zero snapshot rows.
        //
        // Project only scalar values needed by the DTO.
        // ToListAsync executes the complete database query.
        var flat = await (
            from plan in db.Set<PublishedPlan>()
                .AsNoTracking()

            where plan.Id == snapshotId

            from row in db.Set<PublishedPlanRow>()
                .Where(r => r.PlanId == plan.Id)
                .DefaultIfEmpty()

            orderby row.RouteId

            select new
            {
                plan.Id,
                plan.FromUtc,
                plan.ToUtc,
                plan.PublishedAtUtc,

                RouteId = row == null ? null : row.RouteId,
                RouteName = row == null ? null : row.RouteName,

                Count = row == null
                    ? 0
                    : row.ScheduledCount,

                Next = row == null
                    ? null
                    : row.NextDepartureUtc
            })
            .ToListAsync(ct);

        // No rows means that the requested snapshot
        // does not exist in the database.
        if (flat.Count == 0)
            throw new SnapshotNotFoundException(snapshotId);

        // All rows belong to the same plan header.
        var header = flat[0];

        // Convert stored scalar values into read DTOs.
        // Rows with no RouteId represent the empty side
        // of the LEFT JOIN and must not become route DTOs.
        var routes = flat
            .Where(r => r.RouteId is not null)
            .Select(r => new RouteOverviewDto(
                r.RouteId!,
                r.RouteName!,
                r.Count,
                r.Next))
            .ToList();

        // Return the original publication timestamp,
        // time window and displayed route values.
        return new PublishedSnapshot(
            header.Id,
            new TimeWindow(
                header.FromUtc,
                header.ToUtc),
            header.PublishedAtUtc,
            routes);
    }
}
