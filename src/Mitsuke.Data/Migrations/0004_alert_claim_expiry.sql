-- When an alert was last claimed for sending. A 'pending' claim older than the expiry means the sender
-- died mid-send (crash, redeploy, timeout); it can then be reclaimed instead of staying stuck forever.
alter table alerts add column claimed_at timestamptz not null default now();
