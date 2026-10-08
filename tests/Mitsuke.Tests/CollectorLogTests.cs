using Mitsuke.Core;

namespace Mitsuke.Tests;

public class CollectorLogTests
{
    [Fact]
    public void Rejections_are_summarised_most_common_first()
    {
        var rejected = new Dictionary<string, int> { ["landed A$41,200 > A$35,000"] = 1, ["model code BL32"] = 3, ["grade 3 < 3.5"] = 1 };

        Assert.Equal("model code BL32 ×3, grade 3 < 3.5 ×1, landed A$41,200 > A$35,000 ×1", Collector.SummariseRejections(rejected));
    }

    [Fact]
    public void Long_summaries_are_capped() =>
        Assert.Equal("a ×1, b ×1 (+2 more)", Collector.SummariseRejections(new Dictionary<string, int> { ["a"] = 1, ["b"] = 1, ["c"] = 1, ["d"] = 1 }, max: 2));

    [Fact]
    public void Nothing_rejected_says_so() => Assert.Equal("none", Collector.SummariseRejections(new Dictionary<string, int>()));
}
