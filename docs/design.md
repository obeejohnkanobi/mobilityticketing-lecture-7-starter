# Design decision

Add your sketch of the command/query paths and the tables they use.

Compare two designs in about 150 words. Explain your choice, what a cancellation
would update, and how a published plan keeps its original values. Mark the
transaction boundaries for booking and publication.

Doodles:
# ER-diagram (database structure)
De er indsat som mermaid-diagrammer i `docs/design.md`. De viser vores Command/Query Paths og de tabeller, de bruger. Vi har separate tabeller for Routes, Trips, Bookings og Published Plans. Vores Live Board henter data on-demand fra Routes og Trips, mens Published Plans gemmer snapshots af de oprindelige værdier i separate tabeller.
```mermaid
erDiagram
ROUTES ||--o{ TRIPS : contains
TRIPS ||--o{ BOOKINGS : has
PUBLISHED_PLANS ||--o{ PUBLISHED_PLAN_ROUTES : contains

    ROUTES {
        text id PK
        text short_name
    }

    TRIPS {
        text id PK
        text route_id FK
        timestamptz scheduled_departure_utc
        text status
    }

    BOOKINGS {
        text request_id PK
        text trip_id FK
        timestamptz booked_at_utc
    }

    PUBLISHED_PLANS {
        text plan_id PK
        timestamptz window_start_utc
        timestamptz window_end_utc
    }

    PUBLISHED_PLAN_ROUTES {
        text plan_id PK,FK
        text route_id PK
        text route_name
        integer scheduled_count
        timestamptz next_departure_utc
    }
```
# Command/Query-diagram (application flow) 
```mermaid
flowchart TB
A["StudentApplication"]

    A --> Q["QUERIES"]
    A --> C["COMMANDS"]

    Q --> Q1["ReadLiveBoard"]
    Q --> Q2["ReadPublishedPlan"]

    C --> C1["CancelTrip"]
    C --> C2["RenameRoute"]
    C --> C3["BookTrip"]
    C --> C4["PublishPlan"]

    Q1 --> R[("lab07.routes")]
    Q1 --> T[("lab07.trips")]

    Q2 --> P[("lab07.published_plans")]
    Q2 --> PR[("lab07.published_plan_routes")]

    C1 --> T
    C2 --> R

    C3 --> T
    C3 --> B[("lab07.bookings")]

    C4 --> R
    C4 --> T
    C4 --> P
    C4 --> PR

```

Vi sammenlignede to forskellige designs: at beregne vores Live Route Overview on-demand og at vedligeholde en separat Current-Summary Table.

Vi valgte On-Demand Calculation med EF Core Projection, fordi vores Departure Board altid skal vise de aktuelle route-navne, antal scheduled trips og næste afgang inden for et vilkårligt UTC-tidsvindue. Når en trip bliver cancelled, eller en route bliver renamed, opdateres vores Source Tables, så efterfølgende queries automatisk viser ændringerne.

En Current-Summary Table kunne forbedre performance ved gentagne queries, men ville kræve ekstra Synchronization, især når vi arbejder med forskellige tidsvinduer.

Til vores Live Board foretrækker vi at bruge Projection direkte til RouteOverviewDto frem for at benytte Eager Loading med Include, hvor vi indlæser komplette Route- og Trip-entities. Projection reducerer unødvendig Data Transfer og Object Materialization, samtidig med at vi kan udføre Aggregation direkte i SQL.

Vores Published Plans benytter separate, persistente Snapshot Tables, så de oprindelige værdier bevares, selvom data senere ændres.

På Command-siden håndterer vi Booking Rules, Idempotency, Retries og Transactional Consistency. Vi benytter desuden Database Constraints og passende Concurrency Control for at forhindre duplicate requests og inkonsistente databaseændringer.

