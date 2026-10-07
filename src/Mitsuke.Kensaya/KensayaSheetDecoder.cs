using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mitsuke.Core;

namespace Mitsuke.Kensaya;

public sealed class KensayaOptions
{
    /// <summary>Kensa-ya's site, e.g. https://kensa-ya.vercel.app (KENSAYA_BASE_URL).</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>Shared secret for /api/partner/sheet (KENSAYA_PARTNER_KEY). Must match Kensa-ya's PARTNER_API_KEY.</summary>
    public string PartnerKey { get; set; } = "";

    public bool IsConfigured => BaseAddress is not null && PartnerKey.Length >= 32;
}

/// <summary>
/// Reads auction sheets through Kensa-ya's partner API (Kensa-ya ADR 0011). Kensa-ya owns the prompt and the
/// translation tables; Mitsuke only sends a sheet URL and maps the answer, so improvements there arrive here for free.
/// Retries, timeouts and the circuit breaker live on the HttpClient (see <see cref="ServiceCollectionExtensions"/>).
/// </summary>
public sealed class KensayaSheetDecoder(HttpClient http, TimeProvider clock) : ISheetDecoder
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SheetReport?> DecodeAsync(Uri sheetUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sheetUrl);
        using var response = await http.PostAsJsonAsync("/api/partner/sheet", new { sheetUrl = sheetUrl.ToString() }, Json, cancellationToken);

        // 422: Kensa-ya looked and couldn't read it (blurry, not a sheet). That's an answer, not an outage.
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity) return null;
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<PartnerSheetResponse>(Json, cancellationToken)
                   ?? throw new InvalidOperationException("Kensa-ya returned an empty sheet response.");
        return Map(sheetUrl, body, clock.GetUtcNow());
    }

    internal static SheetReport Map(Uri sheetUrl, PartnerSheetResponse r, DateTimeOffset decodedAt)
    {
        var x = r.Extraction;
        return new SheetReport
        {
            SheetUrl = sheetUrl,
            DecodedAt = decodedAt,
            IsAuctionSheet = x.Document.IsAuctionSheet,
            Summary = x.SummaryEn ?? "",
            RedFlags = r.RedFlags.Select(f => new SheetFlag(Severity(f.Severity), f.Title, f.Detail)).ToList(),
            Damage = r.Damage.Select(d => new SheetDamage(d.Code, d.LocationLabel ?? d.Location, d.Name, d.Size, d.Summary, d.Note)).ToList(),
            OverallGrade = x.Grades.Overall,
            InteriorGrade = x.Grades.Interior,
            Colour = x.Vehicle.Colour,
            MileageKm = x.Vehicle.MileageKm,
            Modifications = x.Modifications,
            Positives = x.Positives,
            WatchOut = x.WatchOut,
            Unclear = x.UnclearFields,
            Warnings = r.Warnings,
            Model = r.Model,
            CostUsd = r.CostUsd,
        };
    }

    private static FlagSeverity Severity(string? s) => s switch
    {
        "high" => FlagSeverity.High,
        "medium" => FlagSeverity.Medium,
        _ => FlagSeverity.Info,
    };
}

// Wire shapes for /api/partner/sheet: only the fields Mitsuke uses. Unknown fields are ignored,
// so Kensa-ya can add to its response without breaking Mitsuke.

internal sealed record PartnerSheetResponse
{
    public required Extraction Extraction { get; init; }
    public List<RedFlagDto> RedFlags { get; init; } = [];
    public List<DamageDto> Damage { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public string? Model { get; init; }
    public decimal CostUsd { get; init; }
}

internal sealed record Extraction
{
    public required DocumentDto Document { get; init; }
    public VehicleDto Vehicle { get; init; } = new();
    public GradesDto Grades { get; init; } = new();
    public List<string> Modifications { get; init; } = [];
    public List<string> Positives { get; init; } = [];
    [System.Text.Json.Serialization.JsonPropertyName("watch_out")]
    public List<string> WatchOut { get; init; } = [];
    [System.Text.Json.Serialization.JsonPropertyName("unclear_fields")]
    public List<string> UnclearFields { get; init; } = [];
    [System.Text.Json.Serialization.JsonPropertyName("summary_en")]
    public string? SummaryEn { get; init; }
}

internal sealed record DocumentDto
{
    [System.Text.Json.Serialization.JsonPropertyName("is_auction_sheet")]
    public bool IsAuctionSheet { get; init; }
}

internal sealed record VehicleDto
{
    public string? Colour { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("mileage_km")]
    public int? MileageKm { get; init; }
}

internal sealed record GradesDto
{
    public string? Overall { get; init; }
    public string? Interior { get; init; }
}

internal sealed record RedFlagDto(string? Severity, string Title, string Detail);

internal sealed record DamageDto
{
    public string Code { get; init; } = "";
    public string Location { get; init; } = "";
    public string? LocationLabel { get; init; }
    public string? Name { get; init; }
    public string? Size { get; init; }
    public string? Summary { get; init; }
    public string? Note { get; init; }
}
