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

    /// <returns>The key, and the file's path when this call created it, for the startup log.</returns>
    public static (string Key, string? GeneratedIn) Resolve(string? configured, string stateDirectory)
    {
        if (!string.IsNullOrEmpty(configured)) return (configured, null);

        Directory.CreateDirectory(stateDirectory);
        var path = Path.Combine(stateDirectory, FileName);
        var created = TryCreate(path);
        return (Read(path), created ? path : null);
    }

    // Written aside, then moved in without overwriting: a concurrent start either wins the move or
    // reads the winner's complete file, never a half-written one.
    private static bool TryCreate(string path)
    {
        if (Path.Exists(path)) return false;

        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var writer = new StreamWriter(temporary, Encoding.ASCII, options))
                writer.Write(Convert.ToBase64String(RandomNumberGenerator.GetBytes(GeneratedBytes)));

            File.Move(temporary, path, overwrite: false);
            return true;
        }
        catch (IOException) when (Path.Exists(path))
        {
            return false;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string Read(string path)
    {
        string key;
        try
        {
            key = File.ReadAllText(path).Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The session signing key {path} cannot be read: {e.Message} " +
                "Give the service user read access to it.", e);
        }

        if (Encoding.UTF8.GetByteCount(key) < AuthorizationExtension.MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"The session signing key {path} is empty or shorter than " +
                $"{AuthorizationExtension.MinimumSigningKeyBytes} bytes. Restore it from a backup, or delete " +
                "it to have a new one generated — which signs every user out.");
        }

        return key;
    }
}
