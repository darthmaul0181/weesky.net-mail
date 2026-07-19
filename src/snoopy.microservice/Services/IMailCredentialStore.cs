using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// Carries the user's mail password between requests, encrypted into a cookie.
    ///
    /// The password cannot be recovered from the database — MariaDB stores SHA-512 crypt —
    /// so it is captured at login. It is encrypted with Data Protection and kept in the
    /// client's cookie rather than in server-side session state, so the server never holds
    /// both the key and the ciphertext at rest.
    /// </summary>
    public interface IMailCredentialStore
    {
        /// <summary>Encrypts the password into the credentials cookie.</summary>
        void Store(HttpResponse response, string password, TimeSpan lifetime);

        /// <summary>
        /// Decrypts the credentials cookie. Fails with "credentials_unavailable" when the
        /// cookie is absent or no longer decryptable, which the caller must surface as a 401
        /// so the client can sign in again rather than show an opaque IMAP error.
        /// </summary>
        Result<string> Retrieve(HttpRequest request);

        /// <summary>Expires the credentials cookie.</summary>
        void Clear(HttpResponse response);
    }
}
