
using MobilityTicketing.Lecture07.Commands;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;
using MobilityTicketing.Lecture07.Diagnostics;
using MobilityTicketing.Lecture07.Queries;

namespace MobilityTicketing.Lecture07;

/// <summary>
/// Application facade for the CQRS operations.
///
/// A facade provides one common entry point for the application.
/// The actual business operations are delegated to dedicated
/// command and query classes.
///
/// The existing interface and constructor are preserved because
/// the CLI and acceptance tests depend on their signatures.
/// </summary>
public sealed class StudentApplication(
    string connection,
    TimeProvider? clock = null,
    ApplicationHooks? hooks = null,
    RunMeasurement? measurement = null
) : ILectureApplication
{
    // Database connection shared by the commands and queries.
    public string Connection { get; } = connection;

    // Injectable clock makes time-dependent behavior testable.
    public TimeProvider Clock { get; } =
        clock ?? TimeProvider.System;

    // Hooks allow tests to simulate failures before commit.
    public ApplicationHooks Hooks { get; } =
        hooks ?? new();

    // Collects SQL measurements for the read-side tests.
    public RunMeasurement Measurement { get; } =
        measurement ?? new();

    /// <summary>
    /// Creates an EF Core DbContext with SQL instrumentation.
    /// Read queries must use this factory so measurements work.
    /// </summary>
    public LectureDbContext CreateContext() =>
        LectureDbContext.Create(
            Connection,
            measurement: Measurement);

    // QUERY: Read current route data without changing the database.
    public async Task<List<RouteOverviewDto>> ReadOverviewAsync(
        TimeWindow window,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(window);

        // Validate before any database access.
        window.Validate();

        // Ensure DbContext is disposed even if the query fails.
        await using var db = CreateContext();

        return await ReadRouteOverviewQuery.ExecuteAsync(
            db, window, ct);
    }

    // COMMAND: Change a trip's status without deleting the row.
    public Task<CancelResult> CancelTripAsync(
        string tripId,
        CancellationToken ct = default) =>
        TripCommands.CancelAsync(Connection, tripId, ct);

    // COMMAND: Update a route's current name.
    public Task<RenameResult> RenameRouteAsync(
        string routeId,
        string name,
        CancellationToken ct = default) =>
        TripCommands.RenameAsync(Connection, routeId, name, ct);

    // COMMAND: Book a trip with idempotent retry behavior.
    public Task<BookingResult> BookTripAsync(
        string tripId,
        string requestId,
        CancellationToken ct = default) =>
        BookingCommand.ExecuteAsync(
            Connection, tripId, requestId, Clock, ct);

    // COMMAND: Persist a historical snapshot in one transaction.
    public Task<PublishResult> PublishSnapshotAsync(
        string snapshotId,
        TimeWindow window,
        CancellationToken ct = default) =>
        PublishPlanCommand.ExecuteAsync(
            Connection, snapshotId, window, Clock, Hooks, ct);

    // QUERY: Read a previously published snapshot.
    public async Task<PublishedSnapshot> ReadSnapshotAsync(
        string snapshotId,
        CancellationToken ct = default)
    {
        DatabaseSession.RequireText(
            snapshotId, nameof(snapshotId));

        await using var db = CreateContext();

        return await ReadPublishedPlanQuery.ExecuteAsync(
            db, snapshotId, ct);
    }
}
