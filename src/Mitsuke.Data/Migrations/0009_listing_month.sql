-- Month of manufacture, when the source gives it. Matters for age rules near a cut-off (Australia's 25-year rule)
-- and for telling generations apart at a changeover (an Evo built in Jan 2003 could be a VII or a VIII).
alter table listings add column month smallint check (month between 1 and 12);
