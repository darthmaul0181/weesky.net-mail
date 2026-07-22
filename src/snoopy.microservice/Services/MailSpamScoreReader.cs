using System.Globalization;
using System.Text.RegularExpressions;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Reads the spam score out of the topmost header of each known anti-spam engine.</summary>
internal static partial class MailSpamScoreReader
{
    // Our own platform runs rspamd, so its header outranks whatever an upstream relay added.
    public static MailSpamScore? Parse(HeaderList headers) =>
        FromRspamd(headers) ?? FromSpamAssassin(headers) ?? FromExchangeScl(headers);

    private static MailSpamScore? FromRspamd(HeaderList headers)
    {
        var header = headers.Topmost("X-Spamd-Result");
        if (header is null) return null;

        var match = RspamdScore().Match(header.Value);
        return match.Success
            ? new MailSpamScore(Number(match.Groups[1]), Number(match.Groups[2]), Raw(header))
            : null;
    }

    private static MailSpamScore? FromSpamAssassin(HeaderList headers)
    {
        var status = headers.Topmost("X-Spam-Status");
        if (status is not null)
        {
            var score = SpamAssassinScore().Match(status.Value);
            var required = SpamAssassinRequired().Match(status.Value);
            if (score.Success && required.Success)
                return new MailSpamScore(Number(score.Groups[1]), Number(required.Groups[1]), Raw(status));
        }

        // X-Spam-Score alone carries no threshold; 5.0 is SpamAssassin's universal default.
        var bare = headers.Topmost("X-Spam-Score");
        return bare is not null
            && double.TryParse(bare.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? new MailSpamScore(value, 5.0, Raw(bare))
            : null;
    }

    private static MailSpamScore? FromExchangeScl(HeaderList headers)
    {
        var header = headers.Topmost("X-MS-Exchange-Organization-SCL");
        if (header is null
            || !int.TryParse(header.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var scl))
        {
            return null;
        }

        // SCL -1 is Microsoft's trusted-internal marker; 5 and up is classed as spam.
        return new MailSpamScore(Math.Max(0, scl), 5, Raw(header));
    }

    private static string Raw(Header header) => $"{header.Field}: {header.Value}";

    private static double Number(Group group) => double.Parse(group.Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\[(-?\d+(?:\.\d+)?)\s*/\s*(-?\d+(?:\.\d+)?)\]")]
    private static partial Regex RspamdScore();

    [GeneratedRegex(@"\bscore=(-?\d+(?:\.\d+)?)")]
    private static partial Regex SpamAssassinScore();

    [GeneratedRegex(@"\brequired=(-?\d+(?:\.\d+)?)")]
    private static partial Regex SpamAssassinRequired();
}
