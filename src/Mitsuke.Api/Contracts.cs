using System.Net.Mail;

namespace Mitsuke.Api;

public sealed record BidRequestCreated(Guid Id);

public sealed record BidRequestBody(decimal MaxBidJpy, string Name, string Email, string? Note)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (MaxBidJpy is < 10_000 or > 1_000_000_000 || MaxBidJpy != Math.Floor(MaxBidJpy))
            errors["maxBidJpy"] = ["Enter a whole number of yen between ¥10,000 and ¥1,000,000,000."];
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 100) errors["name"] = ["Enter your name (up to 100 characters)."];
        if (string.IsNullOrWhiteSpace(Email) || Email.Length > 254 || !MailAddress.TryCreate(Email.Trim(), out _))
            errors["email"] = ["Enter a valid email address."];
        if (Note is { Length: > 1000 }) errors["note"] = ["Keep the note under 1,000 characters."];
        return errors;
    }
}
