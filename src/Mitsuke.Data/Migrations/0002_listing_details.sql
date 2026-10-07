-- Per-car detail (sheets, relists, full gallery), fetched only for watchlist matches and cached.
-- payload is our own normalised ListingDetails, not the provider's raw response.
create table listing_details (
    listing_id   uuid primary key references listings (id) on delete cascade,
    fetched_at   timestamptz not null,
    sheet_count  int not null,
    relist_count int not null,
    payload      jsonb not null
);
