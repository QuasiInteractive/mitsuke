-- "I want to bid" requests. Mitsuke never bids or holds money: each request is passed to a partner exporter
-- (by hand for now), who places the bid and deals with the buyer directly.
create table bid_requests (
    id           uuid primary key default gen_random_uuid(),
    listing_id   uuid not null references listings (id) on delete cascade,
    max_bid_jpy  numeric(14, 0) not null check (max_bid_jpy > 0),
    name         text not null check (length(name) between 1 and 100),
    email        text not null check (length(email) between 3 and 254),
    note         text check (length(note) <= 1000),
    status       text not null default 'new' check (status in ('new', 'forwarded', 'won', 'lost', 'withdrawn')),
    created_at   timestamptz not null default now()
);

create index bid_requests_listing_idx on bid_requests (listing_id);
