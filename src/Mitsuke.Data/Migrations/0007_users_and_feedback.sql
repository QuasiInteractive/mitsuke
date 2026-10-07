-- People. The id is the auth provider's subject (Supabase Auth user id); Mitsuke stores no passwords.
create table users (
    id         uuid primary key,
    email      text not null,
    created_at timestamptz not null default now()
);

-- Watchlists belong to a user. Null owner = a system/demo watchlist (e.g. the seeded R32 GT-R).
alter table watchlists add column user_id uuid references users (id) on delete cascade;
create index watchlists_user_idx on watchlists (user_id) where user_id is not null;

-- "Keep watching" / "Not for me" on a lot. One opinion per person per lot; the reason tunes future matching.
create table lot_feedback (
    user_id    uuid not null references users (id) on delete cascade,
    listing_id uuid not null references listings (id) on delete cascade,
    kind       text not null check (kind in ('watching', 'not_for_me')),
    reason     text check (length(reason) <= 200),
    created_at timestamptz not null default now(),
    primary key (user_id, listing_id)
);
