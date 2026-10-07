using System.Globalization;
using System.Text;
using static System.Net.WebUtility;

namespace Mitsuke.Core;

public sealed record EmailMessage(string To, string Subject, string Text, string Html);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// The alert as an email: a scannable card (photo, landed cost, deal score, red flags) with a button to the lot page.
/// Email clients ignore modern CSS, so it's tables and inline styles. Every value is HTML-encoded: sheet text comes
/// from an AI read of a third-party document and must never be able to inject markup into someone's inbox.
/// </summary>
public static class AlertEmailFormatter
{
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
    private const string Accent = "#d7263d";

    public static EmailMessage Format(
        string to, Watchlist watchlist, Listing listing, ListingDetails? details, LandedEstimate? landed, DealScore? deal,
        SheetReport? sheet, Uri? lotUrl, Uri? manageUrl)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        ArgumentNullException.ThrowIfNull(listing);

        var title = Title(listing);
        var highFlags = sheet?.RedFlags.Where(f => f.Severity == FlagSeverity.High).ToList() ?? [];
        var subject = (highFlags.Count > 0 ? "⚠ " : "") + $"Found: {title}"
            + (landed is null ? "" : string.Create(Au, $" · est. {Symbol(landed.Total.Currency)}{landed.Total.Amount:N0} landed"));

        var photo = details?.PhotoUrls is { Count: > 0 } detailPhotos ? detailPhotos[0] : listing.PhotoUrls.Count > 0 ? listing.PhotoUrls[0] : null;
        var facts = new[]
        {
            listing.MileageKm is { } km ? string.Create(Au, $"{km:N0} km{(sheet?.MileageIsDoubtful == true ? " (unverified)" : "")}") : null,
            listing.Grade is { } g ? $"Grade {g}" : null,
            listing.Transmission,
            AlertFormatter.AuctionDay(listing) is { } d ? string.Create(Au, $"Auction {d:ddd d MMM} (Japan)") : null,
        }.Where(f => f is not null);

        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"""
            <!doctype html><html><body style="margin:0;padding:0;background:#0b0b0d;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#0b0b0d;padding:24px 12px;">
            <tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#141418;border:1px solid #2a2a33;border-radius:18px;overflow:hidden;font-family:Inter,Segoe UI,Helvetica,Arial,sans-serif;color:#f4f4f5;">
            <tr><td style="padding:20px 24px 8px;font-size:20px;font-weight:700;">Mitsuke <span style="color:{Accent};">見つけ</span>
              <div style="font-size:12px;font-weight:400;color:#a1a1aa;margin-top:4px;">A match for your watchlist “{HtmlEncode(watchlist.Name)}”</div></td></tr>
            """);

        if (photo is not null)
            html.Append(CultureInfo.InvariantCulture, $"""<tr><td style="padding:8px 24px;"><img src="{HtmlEncode(photo.ToString())}" alt="{HtmlEncode(title)}" width="512" style="width:100%;max-width:512px;border-radius:12px;display:block;background:#000;"></td></tr>""");

        html.Append(CultureInfo.InvariantCulture, $"""
            <tr><td style="padding:12px 24px 4px;font-size:22px;font-weight:700;line-height:1.25;">{HtmlEncode(title)}</td></tr>
            <tr><td style="padding:0 24px 12px;font-size:14px;color:#a1a1aa;">{HtmlEncode(string.Join(" · ", facts))}</td></tr>
            """);

        foreach (var f in highFlags)
            html.Append(CultureInfo.InvariantCulture, $"""<tr><td style="padding:4px 24px;"><div style="background:#2a1216;border:1px solid #6b1d29;border-radius:12px;padding:12px 14px;font-size:14px;"><strong style="color:{Accent};">⚠ {HtmlEncode(f.Title)}</strong><br><span style="color:#d4d4d8;">{HtmlEncode(f.Detail)}</span></div></td></tr>""");

        html.Append("""<tr><td style="padding:12px 24px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>""");
        if (landed is not null)
            html.Append(string.Create(Au, $"""<td style="background:#1b1b21;border-radius:12px;padding:14px;" valign="top"><div style="font-size:12px;color:#a1a1aa;">Est. landed in {HtmlEncode(landed.Destination)}</div><div style="font-size:26px;font-weight:700;">{Symbol(landed.Total.Currency)}{landed.Total.Amount:N0}</div><div style="font-size:12px;color:#71717a;">range {Symbol(landed.Low.Currency)}{landed.Low.Amount:N0}–{landed.High.Amount:N0}</div></td>"""));
        if (deal is { Score: { } score })
            html.Append(CultureInfo.InvariantCulture, $"""<td width="12"></td><td style="background:#1b1b21;border-radius:12px;padding:14px;" valign="top"><div style="font-size:12px;color:#a1a1aa;">Deal score</div><div style="font-size:26px;font-weight:700;">{score}<span style="font-size:14px;color:#a1a1aa;">/100</span></div><div style="font-size:12px;color:{Accent};font-weight:600;">{HtmlEncode(deal.Label)}</div></td>""");
        html.Append("</tr></table></td></tr>");

        if (listing.Price is { } price)
            html.Append(string.Create(Au, $"""<tr><td style="padding:0 24px 8px;font-size:13px;color:#a1a1aa;">Opening bid {Symbol(price.Currency)}{price.Amount:N0}{(listing.AuctionHouse is null ? "" : $" · {HtmlEncode(listing.AuctionHouse)}")}{(listing.LotNumber is null ? "" : $" lot {HtmlEncode(listing.LotNumber)}")}</td></tr>"""));

        if (sheet is { Summary.Length: > 0 })
            html.Append(CultureInfo.InvariantCulture, $"""<tr><td style="padding:8px 24px;font-size:14px;line-height:1.5;color:#d4d4d8;"><strong style="color:#f4f4f5;">From the auction sheet:</strong> {HtmlEncode(sheet.Summary)}</td></tr>""");

        if (lotUrl is not null)
            html.Append(CultureInfo.InvariantCulture, $"""<tr><td style="padding:16px 24px 8px;"><a href="{HtmlEncode(lotUrl.ToString())}" style="display:block;text-align:center;background:{Accent};color:#ffffff;text-decoration:none;font-weight:700;font-size:16px;padding:14px;border-radius:12px;">View in Mitsuke →</a></td></tr>""");

        html.Append(CultureInfo.InvariantCulture, $"""
            <tr><td style="padding:12px 24px 20px;font-size:11px;line-height:1.5;color:#71717a;">
              {HtmlEncode(listing.Attribution)} · {HtmlEncode(AlertFormatter.Disclaimer)} Mitsuke never bids or holds money.
              {(manageUrl is null ? "" : $"""<br><a href="{HtmlEncode(manageUrl.ToString())}" style="color:#a1a1aa;">Pause or change your watchlists</a>""")}
            </td></tr>
            </table></td></tr></table></body></html>
            """);

        // Plain-text part for clients that don't render HTML: the same text as every other channel.
        var text = AlertFormatter.Format(watchlist, listing, details, landed, deal, sheet, lotUrl)
                   + (manageUrl is null ? "" : $"\n\nPause or change your watchlists: {manageUrl}");
        return new EmailMessage(to, subject, text, html.ToString());
    }

    private static string Title(Listing l) =>
        string.Join(' ', new[] { l.Year?.ToString(CultureInfo.InvariantCulture), l.Make, l.Model }.Where(s => !string.IsNullOrEmpty(s)))
        + (l.ModelCode is { } c ? $" ({c})" : "");

    private static string Symbol(string currency) => currency switch
    {
        "AUD" => "A$",
        "NZD" => "NZ$",
        "JPY" => "¥",
        _ => currency + " ",
    };
}
