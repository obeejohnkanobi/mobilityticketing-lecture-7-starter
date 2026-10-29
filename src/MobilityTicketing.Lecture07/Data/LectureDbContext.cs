using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Diagnostics;

namespace MobilityTicketing.Lecture07.Data;

public partial class LectureDbContext(DbContextOptions<LectureDbContext> options, RunMeasurement measurement) : DbContext(options)
{
    public RunMeasurement Measurement { get; } = measurement;
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Booking> Bookings => Set<Booking>();
    
    // Historical snapshot entities.
//
// Explicit DbSet properties expose the two snapshot tables.
// Their keys, columns and relationships are configured
// separately in ConfigureReadModels().
    public DbSet<PublishedPlan> PublishedPlans => Set<PublishedPlan>();

    public DbSet<PublishedPlanRow> PublishedPlanRows => Set<PublishedPlanRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("lab07");
        b.Entity<Route>(e =>
        {
            e.ToTable("routes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("short_name");
            e.HasMany(x => x.Trips).WithOne(x => x.Route).HasForeignKey(x => x.RouteId);
        });
        b.Entity<Trip>(e =>
        {
            e.ToTable("trips");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RouteId).HasColumnName("route_id");
            e.Property(x => x.DepartureUtc).HasColumnName("scheduled_departure_utc");
            e.Property(x => x.Status).HasColumnName("status");
        });
        b.Entity<Booking>(e =>
        {
            e.ToTable("bookings");
            e.HasKey(x => x.RequestId);
            e.Property(x => x.RequestId).HasColumnName("request_id");
            e.Property(x => x.TripId).HasColumnName("trip_id");
            e.Property(x => x.BookedAtUtc).HasColumnName("booked_at_utc");
        });
        ConfigureReadModels(b);
    }

    void ConfigureReadModels(ModelBuilder b)
    {
        b.Entity<PublishedPlan>(e =>
        {
            e.ToTable("published_plans");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("plan_id");
            e.Property(x => x.FromUtc).HasColumnName("window_start_utc");
            e.Property(x => x.ToUtc).HasColumnName("window_end_utc");
            e.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");

            e.HasMany(x => x.Rows)
                .WithOne()
                .HasForeignKey(x => x.PlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PublishedPlanRow>(e =>
        {
            e.ToTable("published_plan_routes");
            e.HasKey(x => new { x.PlanId, x.RouteId });
            e.Property(x => x.PlanId).HasColumnName("plan_id");
            e.Property(x => x.RouteId).HasColumnName("route_id");
            e.Property(x => x.RouteName).HasColumnName("route_name");
            e.Property(x => x.ScheduledCount).HasColumnName("scheduled_count");
            e.Property(x => x.NextDepartureUtc).HasColumnName("next_departure_utc");
        });
    }

    public static LectureDbContext Create(string connection, bool lazy = false, RunMeasurement? measurement = null)
    {
        var m = measurement ?? new RunMeasurement();
        var options = new DbContextOptionsBuilder<LectureDbContext>()
            .UseNpgsql(connection)
            .AddInterceptors(new ReadCommandCounter(m), EntityMaterializationCounter.Instance);
        if (lazy) options.UseLazyLoadingProxies();
        return new LectureDbContext(options.Options, m);
    }
}
