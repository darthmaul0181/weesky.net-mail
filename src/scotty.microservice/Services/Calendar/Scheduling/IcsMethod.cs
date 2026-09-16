using weesky.Scotty.Microservice.Services.Calendar.Invitations;

namespace weesky.Scotty.Microservice.Services.Calendar.Scheduling;

/// <summary>The one line an iTIP message adds to a stored file. Textual, so the file the
/// invitees receive is the file in base, byte for byte but for this line (décision 4).</summary>
internal static class IcsMethod
{
    internal static string With(string ics, string method)
    {
        var newline = PartStatRewriter.NewlineOf(ics);
        var lines = ics.Split(newline).Where(l => !l.StartsWith("METHOD:", StringComparison.OrdinalIgnoreCase)).ToList();
        var version = lines.FindIndex(l => l.StartsWith("VERSION:", StringComparison.OrdinalIgnoreCase));
        lines.Insert(version < 0 ? 1 : version + 1, "METHOD:" + method);
        return string.Join(newline, lines);
    }
}
