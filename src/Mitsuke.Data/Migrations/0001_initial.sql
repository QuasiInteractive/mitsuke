-- Mitsuke initial schema. Design notes: docs/thecarapi-findings.md ("Design implications").

-- One physical car. Japanese cars have no VIN, so identity is the frame number when we have it.
-- The same car is relisted under a new listing id most weeks; vehicles tie those listings together.
create table vehicles (
    id            uuid primary key default gen_random_uuid(),
    frame_number  text not null unique,
    make          text not null,
    model         text not null,
    model_code    text,
    first_seen_at timestamptz not null default now()
);

-- One appearance of a car at one source. (source, source_id) is the provider's identity;
-- source_id is text because TheCarApi Japan ids exceed 2^53.
create table listings (
    id              uuid primary key default gen_random_uuid(),
    source          text not null,
    source_id       text not null,
    vehicle_id      uuid references vehicles (id),
    make            text not null,
    model           text not null,
    model_code      text,
    is_modified     boolean not null default false,
    frame_number    text,
    year            int,
    mileage_km      int,
    grade_raw       text,
    grade_score     numeric(3, 1),
    grade_repaired  boolean not null default false,
    auction_house   text,
    lot_number      text,
    auction_ends_at timestamptz,
    transmission    text,
    fuel            text,
    right_hand_drive boolean,
    photo_urls      text[] not null default '{}',
    attribution     text not null,
    first_seen_at   timestamptz not null,
    last_seen_at    timestamptz not null,
    unique (source, source_id)
);

create index listings_make_model_idx on listings (lower(make), lower(model), model_code);
create index listings_vehicle_idx on listings (vehicle_id) where vehicle_id is not null;

-- Our own price history. Rows are only written when the price changes, so this is a log of
-- changes, not a poll log. kind matters: Japanese auction prices are opening bids, never results.
create table price_observations (
    id          bigint generated always as identity primary key,
    listing_id  uuid not null references listings (id) on delete cascade,
    observed_at timestamptz not null,
    kind        text not null check (kind in ('opening_bid', 'asking_price', 'reported_final', 'unknown')),
    amount      numeric(14, 2) not null check (amount >= 0),
    currency    char(3) not null
);

create index price_observations_listing_idx on price_observations (listing_id, observed_at desc);

create table watchlists (
    id                 uuid primary key default gen_random_uuid(),
    name               text not null,
    make               text not null,
    model              text not null,
    model_codes        text[] not null default '{}',
    year_from          int,
    year_to            int,
    max_mileage_km     int,
    min_grade          numeric(3, 1),
    include_repaired   boolean not null default false,
    include_modified   boolean not null default true,
    max_price_amount   numeric(14, 2),
    max_price_currency char(3),
    is_active          boolean not null default true,
    created_at         timestamptz not null default now(),
    check ((max_price_amount is null) = (max_price_currency is null))
);

-- One row per (watchlist, listing, channel): the guarantee that nobody gets the same alert twice.
-- status lets a failed send be retried on the next scan without ever double-sending a delivered one.
create table alerts (
    id           bigint generated always as identity primary key,
    watchlist_id uuid not null references watchlists (id) on delete cascade,
    listing_id   uuid not null references listings (id) on delete cascade,
    channel      text not null,
    status       text not null check (status in ('pending', 'sent', 'failed')),
    attempts     int not null default 1,
    last_error   text,
    created_at   timestamptz not null default now(),
    sent_at      timestamptz,
    unique (watchlist_id, listing_id, channel)
);
