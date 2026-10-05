using System.Security.Cryptography;
using System.Text;
using weesky.Scotty.Microservice.Authentication.Extensions;

namespace weesky.Scotty.Microservice.Configuration;

/// <summary>
/// The key that signs session tokens when TokenConstants:Key gives none: generated once and kept
/// in the state directory, so sessions survive a restart. A file that is there but unusable stops
/// the start — generating over it would sign everyone out with nothing saying why.
/// </summary>
internal static class SessionSigningKey
{
    public const string FileName = "session-signing.key";
    private const int GeneratedBytes = 64;
    private const int ReadAttempts = 10;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <returns>The key, and the file's path when this call created it, for the startup log.</returns>
    public static (string Key, string? GeneratedIn) Resolve(string? configured, string stateDirectory)
    {
        if (!string.IsNullOrEmpty(configured)) return (configured, null);

        Directory.CreateDirectory(stateDirectory);
        var path = Path.Combine(stateDirectory, FileName);
        var created = TryCreate(path);
        return (Read(path), created ? path : null);
    }

    // CreateNew is an exclusive create on every OS: of two concurrent starts exactly one writes, and
    // the other reads what it wrote (Read waits out the instant between that create and that write).
    private static bool TryCreate(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using var writer = new StreamWriter(path, Encoding.ASCII, options);
            writer.Write(Convert.ToBase64String(RandomNumberGenerator.GetBytes(GeneratedBytes)));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && Path.Exists(path))
        {
            return false;
        }
    }

    private static string Read(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var key = File.ReadAllText(path).Trim();
                if (Encoding.UTF8.GetByteCount(key) >= AuthorizationExtension.MinimumSigningKeyBytes) return key;
                if (attempt == ReadAttempts)
                {
                    throw new InvalidOperationException(
                        $"The session signing key {path} is empty or shorter than " +
                        $"{AuthorizationExtension.MinimumSigningKeyBytes} bytes. Delete it to have a new one " +
                        "generated — which signs every user out.");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt == ReadAttempts)
            {
                throw new InvalidOperationException(
                    $"The session signing key {path} cannot be read: {e.Message} " +
                    "Give the service user read access to it.", e);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Another start may still hold it open for its write; the last attempt reports it.
            }

            Thread.Sleep(ReadRetryDelay);
        }
    }
}
