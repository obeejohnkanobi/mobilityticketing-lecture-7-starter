-- Design persistent storage for published dispatch plans in lab07.
-- Include the plan identity, time window, publication time and displayed values.
-- The contract defines behavior; the table layout is your choice.

-- ============================================================
-- Lecture 7: Persistent Read Models
-- ============================================================
--
-- This file creates historical snapshot storage.
--
-- The live route overview reads from routes and trips.
-- Published snapshots must preserve the original values,
-- even when the source tables subsequently change.
--
-- FixtureReset executes this file after 01-schema.sql.
-- ============================================================


-- ------------------------------------------------------------
-- 1. Published plan header
-- ------------------------------------------------------------

CREATE TABLE lab07.published_plans
(
    -- Unique identifier of the published plan.
    plan_id text PRIMARY KEY,

    -- Original publication window.
    window_start_utc timestamptz NOT NULL,
    window_end_utc   timestamptz NOT NULL,

    -- When the plan was originally published.
    published_at_utc timestamptz NOT NULL,

    -- Reject empty identifiers.
    CONSTRAINT published_plan_id_not_blank
        CHECK (btrim(plan_id) <> ''),

    -- Require a positive window of at most 24 hours.
    CONSTRAINT published_plan_window_valid
        CHECK (
            window_end_utc > window_start_utc
                AND
            window_end_utc - window_start_utc
                <= interval '24 hours'
            )
);


-- ------------------------------------------------------------
-- 2. Published route snapshot rows
-- ------------------------------------------------------------

CREATE TABLE lab07.published_plan_routes
(
    -- Parent publication.
    plan_id text NOT NULL
        REFERENCES lab07.published_plans(plan_id),

    -- Historical route identifier.
    route_id text NOT NULL,

    -- Route name as displayed during publication.
    route_name text NOT NULL,

    -- Number of scheduled departures when published.
    scheduled_count integer NOT NULL
        CHECK (scheduled_count >= 0),

    -- Earliest scheduled departure, if any.
    next_departure_utc timestamptz NULL,

    -- Prevent duplicate routes in the same plan.
    PRIMARY KEY (plan_id, route_id),

    -- Route names must not be empty.
    CONSTRAINT snapshot_name_not_blank
        CHECK (btrim(route_name) <> '')
);

-- IMPORTANT:
-- There is deliberately no foreign key from route_id
-- to lab07.routes.
--
-- Historical snapshot rows store copied values and
-- must remain independent of current route changes.
