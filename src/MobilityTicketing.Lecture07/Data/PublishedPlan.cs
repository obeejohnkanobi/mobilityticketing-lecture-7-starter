
namespace MobilityTicketing.Lecture07.Data;

/// <summary>
/// Represents the header of a published historical route plan.
///
/// The class is intentionally NOT sealed because the starter
/// project uses EF Core change-tracking proxies.
///
/// EF Core must be able to inherit from this entity class
/// and override its mapped properties.
///
/// Once a plan has been published, application commands
/// must preserve its original values.
/// </summary>
public class PublishedPlan
{
    /// <summary>
    /// Unique identifier of the published plan.
    ///
    /// This is the primary key in PostgreSQL.
    /// </summary>
    public virtual string Id { get; set; } = string.Empty;

    /// <summary>
    /// Beginning of the original UTC time window.
    /// </summary>
    public virtual DateTime FromUtc { get; set; }

    /// <summary>
    /// End of the original UTC time window.
    /// The endpoint is exclusive.
    /// </summary>
    public virtual DateTime ToUtc { get; set; }

    /// <summary>
    /// Timestamp of the original publication.
    ///
    /// This value must be preserved when the same
    /// publication is requested again.
    /// </summary>
    public virtual DateTime PublishedAtUtc { get; set; }

    /// <summary>
    /// Navigation property representing the historical
    /// route rows belonging to this publication.
    ///
    /// Virtual allows EF Core to override the property
    /// when creating proxy instances.
    /// </summary>
    public virtual List<PublishedPlanRow> Rows { get; set; } = new();
}

/// <summary>
/// Represents the historical values of one route
/// within a published plan.
///
/// The values are copied at publication time and
/// must not change when the source tables change.
///
/// The entity is intentionally unsealed and its
/// mapped properties are virtual to support
/// EF Core change-tracking proxies.
/// </summary>
public class PublishedPlanRow
{
    /// <summary>
    /// Foreign key referencing PublishedPlan.Id.
    ///
    /// Together with RouteId, this forms the
    /// composite primary key.
    /// </summary>
    public virtual string PlanId { get; set; } = string.Empty;

    /// <summary>
    /// Historical route identifier.
    ///
    /// This is intentionally not a foreign key
    /// to the current Routes table.
    /// </summary>
    public virtual string RouteId { get; set; } = string.Empty;

    /// <summary>
    /// Route name as it appeared when the plan
    /// was originally published.
    /// </summary>
    public virtual string RouteName { get; set; } = string.Empty;

    /// <summary>
    /// Number of scheduled trips at publication time.
    /// Must never be negative.
    /// </summary>
    public virtual int ScheduledCount { get; set; }

    /// <summary>
    /// The earliest scheduled departure in UTC.
    ///
    /// Null means that the route had no scheduled
    /// departures within the publication window.
    /// </summary>
    public virtual DateTime? NextDepartureUtc { get; set; }
}
