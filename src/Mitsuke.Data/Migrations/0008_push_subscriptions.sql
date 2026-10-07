-- Browser push subscriptions: one per device a person turned notifications on for.
-- The endpoint is the push service's address for that device; p256dh/auth encrypt messages so only it can read them.
create table push_subscriptions (
    id          uuid primary key default gen_random_uuid(),
    user_id     uuid not null references users (id) on delete cascade,
    endpoint    text not null unique check (endpoint like 'https://%'),
    p256dh      text not null,
    auth        text not null,
    user_agent  text,
    created_at  timestamptz not null default now(),
    last_ok_at  timestamptz
);

create index push_subscriptions_user_idx on push_subscriptions (user_id);
