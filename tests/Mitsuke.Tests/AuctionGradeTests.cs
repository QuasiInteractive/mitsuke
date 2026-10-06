using Mitsuke.Core;

namespace Mitsuke.Tests;

public class AuctionGradeTests
{
    [Theory]
    [InlineData("4", 4.0, false)]
    [InlineData("3.5", 3.5, false)]
    [InlineData(" 4.5 ", 4.5, false)]
    [InlineData("S", 6.0, false)]
    public void Numeric_grades_have_a_score(string raw, double score, bool repaired)
    {
        var grade = AuctionGrade.Parse(raw)!;
        Assert.Equal((decimal)score, grade.Score);
        Assert.Equal(repaired, grade.IsRepaired);
    }

    [Theory]
    [InlineData("R")]
    [InlineData("RA")]
    [InlineData("ra")]
    public void R_grades_are_repaired_with_no_score(string raw)
    {
        var grade = AuctionGrade.Parse(raw)!;
        Assert.True(grade.IsRepaired);
        Assert.Null(grade.Score);
    }

    [Fact]
    public void Stars_are_ungraded_not_repaired()
    {
        var grade = AuctionGrade.Parse("***")!;
        Assert.False(grade.IsRepaired);
        Assert.Null(grade.Score);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Missing_grade_is_null(string? raw) => Assert.Null(AuctionGrade.Parse(raw));
}
