-- Watchlists budget in the buyer's own currency, landed (the brief: "under $45K AUD landed").
alter table watchlists
    add column destination         char(2) not null default 'AU',
    add column max_landed_amount   numeric(14, 2),
    add column max_landed_currency char(3),
    add check ((max_landed_amount is null) = (max_landed_currency is null));
