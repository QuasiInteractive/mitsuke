// Restated from Kensa-ya's Sheets/SheetExtraction.cs by scripts/sync-kensaya-engine.sh: what a vehicle's
// year means. Eligibility.cs needs it; the rest of Kensa-ya's sheet code stays in Kensa-ya.
using System.Text.Json.Serialization;

namespace Kensaya.Worker.Core.Rules;

[JsonConverter(typeof(JsonStringEnumConverter<YearBasis>))]
public enum YearBasis
{
    [JsonStringEnumMemberName("first_registration")] FirstRegistration,
    [JsonStringEnumMemberName("manufacture")] Manufacture,
    [JsonStringEnumMemberName("unclear")] Unclear,
}

