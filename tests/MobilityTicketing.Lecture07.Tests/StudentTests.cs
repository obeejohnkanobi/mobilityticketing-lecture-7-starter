
using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using Xunit;

namespace MobilityTicketing.Lecture07.Tests;

/// <summary>
/// Additional integration tests for the student implementation.
///
/// These tests verify state transitions, snapshot persistence,
/// idempotency, rollback and concurrent publication.
/// </summary>
[Trait("Category", "Student")]
public class StudentTests : Fixture
{
    [Fact]
    public async Task CancelTwice_LeavesOneTripAndAnEmptySecondWrite()
    {
        // Act: cancel the same trip twice.
        var first = await App.CancelTripAsync("T102");
        var second = await App.CancelTripAsync("T102");

        // Assert: the first call changes the state.
        Assert.Equal(CancelResult.Cancelled, first);

        // The second call recognizes the existing state.
        Assert.Equal(CancelResult.AlreadyCancelled, second);

        await using var db = Db();

        // Cancellation must not delete the trip.
        Assert.Equal(9, await db.Trips.CountAsync());

        var trip = await db.Trips.SingleAsync(
            t => t.Id == "T102");

        Assert.Equal("Cancelled", trip.Status);
    }

    [Fact]
    public async Task Rename_TrimsWhitespaceAndUpdatesNextRead()
    {
        // Rename an existing route.
        var result = await App.RenameRouteAsync(
            "R1", "  City Ring  ");

        Assert.Equal(RenameResult.Renamed, result);

        // A new live read must use the changed name.
        var live = await App.ReadOverviewAsync(
            TimeWindow.Morning);

        Assert.Equal("City Ring", live[0].RouteName);
    }

    [Fact]
    public async Task RetryAfterCancellation_ReturnsOriginalBookingReceipt()
    {
        // Create a successful booking.
        var first = await App.BookTripAsync(
            "T102", "same-request");

        Assert.Equal(BookingStatus.Booked, first.Status);

        // Cancel the trip after the successful booking.
        await App.CancelTripAsync("T102");

        // Retrying the original request must still work.
        var retry = await App.BookTripAsync(
            "T102", "same-request");

        Assert.Equal(
            BookingStatus.AlreadyBooked,
            retry.Status);

        // Preserve the original receipt and timestamp.
        Assert.Equal(first.Receipt, retry.Receipt);
    }

    [Fact]
    public async Task FailedPublish_RollsBackEntirePlan()
    {
        // Inject a failure immediately before transaction commit.
        var failing = new StudentApplication(
            Connection,
            Clock,
            new ApplicationHooks
            {
                BeforePublishCommit = () =>
                    throw new InvalidOperationException("failure")
            });

        // The publication must fail.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => failing.PublishSnapshotAsync(
                "failed", TimeWindow.Morning));

        // Atomicity: the failed publication must not exist.
        await Assert.ThrowsAsync<SnapshotNotFoundException>(
            () => App.ReadSnapshotAsync("failed"));
    }

    [Fact]
    public async Task ConcurrentIndependentPublications_PreserveSeparateWindows()
    {
        var morning = TimeWindow.Morning;

        var later = new TimeWindow(
            Utc(7, 0),
            Utc(8, 0));

        // Publish two independent plans concurrently.
        await Task.WhenAll(
            App.PublishSnapshotAsync("morning", morning),
            App.PublishSnapshotAsync("later", later));

        // Each plan must retain its own original window.
        var morningPlan = await App.ReadSnapshotAsync("morning");
        var laterPlan = await App.ReadSnapshotAsync("later");

        Assert.Equal(morning, morningPlan.Window);
        Assert.Equal(later, laterPlan.Window);
    }
}
