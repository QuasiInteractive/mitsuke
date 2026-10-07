using Dapper;
using Mitsuke.Core;
using Npgsql;

namespace Mitsuke.Data;

public sealed class PostgresListingStore(NpgsqlDataSource db) : IListingStore
{
    public async Task<UpsertResult> UpsertAsync(Listing listing, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listing);
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        var seenAt = listing.ObservedAt.ToUniversalTime();

        Guid? vehicleId = null;
        if (listing.FrameNumber is { } frame)
        {
            // No-op update so RETURNING gives the id whether the vehicle is new or known.
            vehicleId = await conn.ExecuteScalarAsync<Guid>("""
                insert into vehicles (frame_number, make, model, model_code, first_seen_at)
                values (@frame, @Make, @Model, @ModelCode, @seenAt)
                on conflict (frame_number) do update set frame_number = excluded.frame_number
                returning id
                """, new { frame, listing.Make, listing.Model, listing.ModelCode, seenAt }, tx);
        }

        // The upsert takes a row lock on the listing, so concurrent scans of the same car serialise here
        // and the price-change check below can't double-insert.
        var row = await conn.QuerySingleAsync<(Guid Id, bool Inserted)>("""
            insert into listings (
                source, source_id, vehicle_id, make, model, model_code, is_modified, frame_number, year, mileage_km,
                grade_raw, grade_score, grade_repaired, auction_house, lot_number, auction_ends_at,
                transmission, fuel, right_hand_drive, photo_urls, attribution, first_seen_at, last_seen_at)
            values (
                @Source, @SourceId, @vehicleId, @Make, @Model, @ModelCode, @IsModified, @FrameNumber, @Year, @MileageKm,
                @GradeRaw, @GradeScore, @GradeRepaired, @AuctionHouse, @LotNumber, @AuctionEndsAt,
                @Transmission, @Fuel, @RightHandDrive, @PhotoUrls, @Attribution, @seenAt, @seenAt)
            on conflict (source, source_id) do update set
                vehicle_id = coalesce(excluded.vehicle_id, listings.vehicle_id),
                make = excluded.make, model = excluded.model, model_code = excluded.model_code,
                is_modified = excluded.is_modified, frame_number = coalesce(excluded.frame_number, listings.frame_number),
                year = excluded.year, mileage_km = excluded.mileage_km,
                grade_raw = excluded.grade_raw, grade_score = excluded.grade_score, grade_repaired = excluded.grade_repaired,
                auction_house = excluded.auction_house, lot_number = excluded.lot_number, auction_ends_at = excluded.auction_ends_at,
                transmission = excluded.transmission, fuel = excluded.fuel, right_hand_drive = excluded.right_hand_drive,
                photo_urls = excluded.photo_urls, attribution = excluded.attribution,
                last_seen_at = greatest(listings.last_seen_at, excluded.last_seen_at)
            returning id, (xmax = 0) as inserted
            """, new
        {
            listing.Key.Source,
            listing.Key.SourceId,
            vehicleId,
            listing.Make,
            listing.Model,
            listing.ModelCode,
            listing.IsModified,
            listing.FrameNumber,
            listing.Year,
            listing.MileageKm,
            GradeRaw = listing.Grade?.Raw,
            GradeScore = listing.Grade?.Score,
            GradeRepaired = listing.Grade?.IsRepaired ?? false,
            listing.AuctionHouse,
            listing.LotNumber,
            AuctionEndsAt = listing.AuctionEndsAt?.ToUniversalTime(),
            listing.Transmission,
            listing.Fuel,
            listing.RightHandDrive,
            PhotoUrls = PgArray.Of(listing.PhotoUrls.Select(u => u.ToString())),
            listing.Attribution,
            seenAt,
        }, tx);

        var priceChanged = false;
        if (listing.Price is { } price)
        {
            // Only record a price when it differs from the latest one: a change log, not a poll log.
            priceChanged = await conn.ExecuteAsync("""
                insert into price_observations (listing_id, observed_at, kind, amount, currency)
                select @listingId, @seenAt, @kind, @Amount, @Currency
                where not exists (
                    select 1 from (
                        select kind, amount, currency from price_observations
                        where listing_id = @listingId
                        order by observed_at desc, id desc
                        limit 1
                    ) last
                    where last.kind = @kind and last.amount = @Amount and last.currency = @Currency)
                """, new { listingId = row.Id, seenAt, kind = ToDb(listing.PriceKind), price.Amount, price.Currency }, tx) == 1;
        }

        await tx.CommitAsync(cancellationToken);
        return new UpsertResult(row.Id, row.Inserted, priceChanged);
    }

    internal static string ToDb(PriceKind kind) => kind switch
    {
        PriceKind.OpeningBid => "opening_bid",
        PriceKind.AskingPrice => "asking_price",
        PriceKind.ReportedFinal => "reported_final",
        _ => "unknown",
    };
}

public sealed class PostgresWatchlistStore(NpgsqlDataSource db) : IWatchlistStore
{
    public async Task<IReadOnlyList<Watchlist>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var rows = await conn.QueryAsync<WatchlistRow>("""
            select id as Id, name as Name, make as Make, model as Model, model_codes as ModelCodes,
                   year_from as YearFrom, year_to as YearTo, max_mileage_km as MaxMileageKm, min_grade as MinGrade,
                   include_repaired as IncludeRepaired, include_modified as IncludeModified,
                   max_price_amount as MaxPriceAmount, max_price_currency as MaxPriceCurrency,
                   destination as Destination, max_landed_amount as MaxLandedAmount, max_landed_currency as MaxLandedCurrency
            from watchlists
            where is_active
            order by created_at
            """);

        return rows.Select(r => new Watchlist
        {
            Id = r.Id,
            Name = r.Name,
            Make = r.Make,
            Model = r.Model,
            ModelCodes = r.ModelCodes.ToHashSet(StringComparer.OrdinalIgnoreCase),
            YearFrom = r.YearFrom,
            YearTo = r.YearTo,
            MaxMileageKm = r.MaxMileageKm,
            MinGrade = r.MinGrade,
            IncludeRepaired = r.IncludeRepaired,
            IncludeModified = r.IncludeModified,
            MaxPrice = r.MaxPriceAmount is { } amount && r.MaxPriceCurrency is { } currency ? new Money(amount, currency) : null,
            Destination = r.Destination,
            MaxLanded = r.MaxLandedAmount is { } landed && r.MaxLandedCurrency is { } landedCurrency ? new Money(landed, landedCurrency) : null,
        }).ToList();
    }

    public async Task AddAsync(Watchlist watchlist, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("""
            insert into watchlists (id, name, make, model, model_codes, year_from, year_to, max_mileage_km, min_grade,
                                    include_repaired, include_modified, max_price_amount, max_price_currency,
                                    destination, max_landed_amount, max_landed_currency)
            values (@Id, @Name, @Make, @Model, @ModelCodes, @YearFrom, @YearTo, @MaxMileageKm, @MinGrade,
                    @IncludeRepaired, @IncludeModified, @MaxPriceAmount, @MaxPriceCurrency,
                    @Destination, @MaxLandedAmount, @MaxLandedCurrency)
            """, new
        {
            watchlist.Id,
            watchlist.Name,
            watchlist.Make,
            watchlist.Model,
            ModelCodes = PgArray.Of(watchlist.ModelCodes),
            watchlist.YearFrom,
            watchlist.YearTo,
            watchlist.MaxMileageKm,
            watchlist.MinGrade,
            watchlist.IncludeRepaired,
            watchlist.IncludeModified,
            MaxPriceAmount = watchlist.MaxPrice?.Amount,
            MaxPriceCurrency = watchlist.MaxPrice?.Currency,
            watchlist.Destination,
            MaxLandedAmount = watchlist.MaxLanded?.Amount,
            MaxLandedCurrency = watchlist.MaxLanded?.Currency,
        });
    }

    private sealed record WatchlistRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public string Make { get; init; } = "";
        public string Model { get; init; } = "";
        public string[] ModelCodes { get; init; } = [];
        public int? YearFrom { get; init; }
        public int? YearTo { get; init; }
        public int? MaxMileageKm { get; init; }
        public decimal? MinGrade { get; init; }
        public bool IncludeRepaired { get; init; }
        public bool IncludeModified { get; init; }
        public decimal? MaxPriceAmount { get; init; }
        public string? MaxPriceCurrency { get; init; }
        public string Destination { get; init; } = "AU";
        public decimal? MaxLandedAmount { get; init; }
        public string? MaxLandedCurrency { get; init; }
    }
}

public sealed class PostgresAlertLog(NpgsqlDataSource db) : IAlertLog
{
    public async Task<long?> TryClaimAsync(Guid watchlistId, Guid listingId, string channel, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        // New alert -> claimed. Previously failed -> reclaimed for a retry. Sent or in flight -> no row, so null.
        return await conn.QuerySingleOrDefaultAsync<long?>("""
            insert into alerts (watchlist_id, listing_id, channel, status)
            values (@watchlistId, @listingId, @channel, 'pending')
            on conflict (watchlist_id, listing_id, channel) do update
                set status = 'pending', attempts = alerts.attempts + 1, last_error = null
                where alerts.status = 'failed'
            returning id
            """, new { watchlistId, listingId, channel });
    }

    public async Task MarkSentAsync(long alertId, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("update alerts set status = 'sent', sent_at = now() where id = @alertId", new { alertId });
    }

    public async Task MarkFailedAsync(long alertId, string reason, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("update alerts set status = 'failed', last_error = left(@reason, 1000) where id = @alertId", new { alertId, reason });
    }
}
