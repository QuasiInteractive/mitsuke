using System.ComponentModel.DataAnnotations;

namespace Mitsuke.Sources.TheCarApi;

public sealed class TheCarApiOptions
{
    public const string SectionName = "TheCarApi";

    [Required]
    public Uri BaseAddress { get; set; } = new("https://api.thecarapi.com");

    /// <summary>From env var THECARAPI_KEY locally, Key Vault when deployed. Never in config files.</summary>
    [Required]
    public string ApiKey { get; set; } = "";

    [Range(1, 100)]
    public int PageSize { get; set; } = 100;

    /// <summary>Hard stop on pages per search so a bad filter can never turn into a bulk crawl.</summary>
    [Range(1, 50)]
    public int MaxPagesPerSearch { get; set; } = 5;

    /// <summary>Client-side ceiling on requests per minute. The key has no server quota; this is our own good behaviour.</summary>
    [Range(1, 600)]
    public int RequestsPerMinute { get; set; } = 30;
}
