using Mitsuke.Core;

namespace Mitsuke.Api;

/// <summary>Everything the lot page shows, in one response so the page renders from a single request.</summary>
public sealed record LotView
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Make { get; init; }
    public required string Model { get; init; }
    public string? ModelCode { get; init; }
    public bool IsModified { get; init; }
    public int? Year { get; init; }
    public int? MileageKm { get; init; }
    public string? Grade { get; init; }
    public bool GradeIsRepaired { get; init; }
    public string? Transmission { get; init; }
    public bool? RightHandDrive { get; init; }
    public string? AuctionHouse { get; init; }
    public string? LotNumber { get; init; }

    /// <summary>End of the auction day (midnight in Japan); the page counts down to the start of that day.</summary>
    public DateTimeOffset? AuctionEndsAt { get; init; }
    public DateOnly? AuctionDay { get; init; }

    public Money? OpeningBid { get; init; }
    public int? Month { get; init; }
    public VariantGuess? Variant { get; init; }
    public IReadOnlyList<SpecItem> Spec { get; init; } = [];
    public LandedEstimate? Landed { get; init; }
    public ImportEligibility? Eligibility { get; init; }
    public DealScore? Deal { get; init; }
    public SheetReport? Sheet { get; init; }

    public IReadOnlyList<Uri> Photos { get; init; } = [];
    public Uri? SheetImage { get; init; }
    public IReadOnlyList<Relist> Relists { get; init; } = [];
    public IReadOnlyList<PricePoint> PriceHistory { get; init; } = [];
    public string? InteriorGrade { get; init; }

    public required string Attribution { get; init; }
    public required string Disclaimer { get; init; }
}

/// <summary>A lot that nearly fits a watchlist, and what it misses by ("landed A$46,420 > A$45,000").</summary>
public sealed record CloseMatch(LotCard Lot, IReadOnlyList<string> MissesBy);

/// <summary>A compact card for lists (home page, watchlist matches).</summary>
public sealed record LotCard(
    Guid Id,
    string Title,
    int? MileageKm,
    string? Grade,
    Uri? Photo,
    Money? OpeningBid,
    Money? LandedTotal,
    int? DealScore,
    string? DealLabel,
    DateOnly? AuctionDay,
    int HighFlags,
    string WatchlistName,
    DateTimeOffset AlertedAt);

/// <summary>Assembles lot views from the stores. Read-only: it never calls paid services (no fetching, no decoding).</summary>
public sealed class LotViewBuilder(
    IListingStore listings,
    IListingDetailsStore details,
    ISheetReportStore sheets,
    ILandedCostEstimator landedCost,
    IComparablesStore comparables,
    IReadQueries queries,
    IEligibilityChecker eligibility,
    IDetailsRefreshRequests? refresh = null)
{
    public async Task<LotView?> BuildAsync(Guid listingId, string destination, CancellationToken cancellationToken)
    {
        var listing = await listings.GetAsync(listingId, cancellationToken);
        if (listing is null) return null;

        var detail = await details.GetAsync(listingId, cancellationToken);
        // Saved before a newer field existed: show what we have now, and have the pipeline fetch the rest.
        if (detail is not null && !DetailsRefresher.IsCurrent(detail) && refresh is not null)
            await refresh.RequestAsync(listingId, cancellationToken);
        var sheet = await sheets.GetAsync(listingId, cancellationToken);
        var landed = listing.Price is { } price ? await landedCost.EstimateAsync(price, destination, cancellationToken) : null;
        var deal = DealScorer.Score(listing, await comparables.GetCandidatesAsync(listing, cancellationToken: cancellationToken));

        return new LotView
        {
            Id = listingId,
            Title = Title(listing),
            Make = listing.Make,
            Model = listing.Model,
            ModelCode = listing.ModelCode,
            IsModified = listing.IsModified,
            Year = listing.Year,
            Month = listing.Month,
            Variant = ModelVariants.Identify(listing, detail),
            Spec = detail?.Spec ?? [],
            MileageKm = listing.MileageKm,
            Grade = listing.Grade?.Raw,
            GradeIsRepaired = listing.Grade?.IsRepaired ?? false,
            Transmission = listing.Transmission,
            RightHandDrive = listing.RightHandDrive,
            AuctionHouse = listing.AuctionHouse,
            LotNumber = listing.LotNumber,
            AuctionEndsAt = listing.AuctionEndsAt,
            AuctionDay = AlertFormatter.AuctionDay(listing),
            OpeningBid = listing.Price,
            Landed = landed,
            Eligibility = await eligibility.CheckAsync(listing, destination, sheet?.Modifications, cancellationToken),
            Deal = deal,
            Sheet = sheet,
            // The full gallery when we have it, otherwise the search preview.
            Photos = detail?.PhotoUrls.Count > 0 ? detail.PhotoUrls : listing.PhotoUrls,
            SheetImage = detail?.CurrentSheet?.ImageUrl,
            Relists = detail?.Relists ?? [],
            PriceHistory = await queries.GetPriceHistoryAsync(listingId, cancellationToken),
            InteriorGrade = sheet?.InteriorGrade ?? detail?.InteriorGrade,
            Attribution = listing.Attribution,
            Disclaimer = AlertFormatter.Disclaimer,
        };
    }

    public async Task<IReadOnlyList<LotCard>> CardsAsync(IEnumerable<MatchSummary> matches, string destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var cards = new List<LotCard>();
        foreach (var match in matches)
        {
            var listing = await listings.GetAsync(match.ListingId, cancellationToken);
            if (listing is null) continue;
            var sheet = await sheets.GetAsync(match.ListingId, cancellationToken);
            var landed = listing.Price is { } price ? await landedCost.EstimateAsync(price, destination, cancellationToken) : null;
            var deal = DealScorer.Score(listing, await comparables.GetCandidatesAsync(listing, cancellationToken: cancellationToken));

            cards.Add(new LotCard(
                match.ListingId,
                Title(listing),
                listing.MileageKm,
                listing.Grade?.Raw,
                listing.PhotoUrls.Count > 0 ? listing.PhotoUrls[0] : null,
                listing.Price,
                landed?.Total,
                deal.Score,
                deal.Score is null ? null : deal.Label,
                AlertFormatter.AuctionDay(listing),
                sheet?.RedFlags.Count(f => f.Severity == FlagSeverity.High) ?? 0,
                match.WatchlistName,
                match.AlertedAt));
        }
        return cards;
    }

    /// <summary>
    /// Lots on auction now that miss the watchlist by one or two things, fewest misses first: so a quiet watchlist still
    /// shows what's out there and why it didn't alert. Lots whose bidding has closed aren't worth showing.
    /// </summary>
    public async Task<IReadOnlyList<CloseMatch>> CloseMatchesAsync(Watchlist watchlist, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        var near = new List<(int Misses, decimal Landed, CloseMatch Match)>();
        foreach (var id in await queries.GetCurrentLotsAsync(watchlist, now, 60, cancellationToken))
        {
            if (await listings.GetAsync(id, cancellationToken) is not { } listing) continue;
            var landed = listing.Price is { } price ? await landedCost.EstimateAsync(price, watchlist.Destination, cancellationToken) : null;
            var misses = WatchlistMatcher.Mismatches(watchlist, listing, now, landed);
            if (misses.Count is 0 or > 2 || misses.Contains("too late to bid")) continue;

            var card = (await CardsAsync([new MatchSummary(id, watchlist.Id, watchlist.Name, now)], watchlist.Destination, cancellationToken)).Single();
            near.Add((misses.Count, landed?.Total.Amount ?? decimal.MaxValue, new CloseMatch(card, misses.Select(Plain).ToList())));
        }
        return near.OrderBy(n => n.Misses).ThenBy(n => n.Landed).Take(12).Select(n => n.Match).ToList();
    }

    /// <summary>The matcher's log wording, for people: "grade 3.5 < 4.0" becomes "Grade 3.5 (you want 4.0+)".</summary>
    internal static string Plain(string reason)
    {
        var parts = reason.Split(" > ", 2);
        if (reason.StartsWith("landed ", StringComparison.Ordinal) && parts.Length == 2)
            return $"Est. {parts[0]["landed ".Length..]} landed, over your {parts[1]} budget";
        if (reason.StartsWith("price ", StringComparison.Ordinal) && parts.Length == 2)
            return $"Opening bid {parts[0]["price ".Length..]}, over your {parts[1]} limit";
        if (reason.StartsWith("mileage ", StringComparison.Ordinal) && parts.Length == 2)
            return $"{parts[0]["mileage ".Length..]}, over your {parts[1]} km";
        if (reason.StartsWith("grade ", StringComparison.Ordinal) && reason.Split(" < ", 2) is [var g, var min])
            return $"Grade {g["grade ".Length..]} (you want {min}+)";
        if (reason.StartsWith("year ", StringComparison.Ordinal))
            return $"Built {reason["year ".Length..].Split(' ')[0]}, outside your years";
        if (reason.StartsWith("repaired", StringComparison.Ordinal)) return "Repaired (accident history)";
        if (reason == "no landed estimate") return "No opening price yet";
        return char.ToUpperInvariant(reason[0]) + reason[1..];
    }

    internal static string Title(Listing l) =>
        string.Join(' ', new[] { l.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture), l.Make, l.Model }.Where(s => !string.IsNullOrEmpty(s)))
        + (l.ModelCode is { } code ? $" ({code})" : "");
}
