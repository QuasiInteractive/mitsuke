-- Makes and models at auction, refreshed daily from the source: what the new-watchlist dropdowns offer.
-- lots = listed right now, so the form can show popular models first.
create table catalog_makes (
    name       text primary key,
    slug       text not null,
    lots       int not null,
    synced_at  timestamptz not null default now()
);

create table catalog_models (
    make       text not null references catalog_makes (name) on delete cascade,
    name       text not null,
    label      text not null,
    slug       text not null,
    lots       int not null,
    synced_at  timestamptz not null default now(),
    primary key (make, name)
);

-- Generations come partly from what Mitsuke has seen: chassis codes per make/model.
create index listings_make_model_code_idx on listings (lower(make), lower(model), model_code);
