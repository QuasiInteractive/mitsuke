-- Auction sheets decoded by Kensa-ya. Each costs real money (an AI call), so one per listing, kept for good.
create table sheet_reports (
    listing_id  uuid primary key references listings (id) on delete cascade,
    sheet_url   text not null,
    decoded_at  timestamptz not null,
    model       text,
    cost_usd    numeric(10, 5) not null default 0,
    high_flags  int not null default 0,
    payload     jsonb not null
);
