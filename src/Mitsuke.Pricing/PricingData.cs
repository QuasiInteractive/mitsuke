namespace Mitsuke.Pricing;

/// <summary>Where the vendored country rules live at runtime (copied next to the assembly at build time).</summary>
public static class PricingData
{
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "data");
}
