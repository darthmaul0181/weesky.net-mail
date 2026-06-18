using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// Talks to a remote Dovecot server via its doveadm HTTP API.
    /// See https://doc.dovecot.org/admin_manual/doveadm_http_api/
    /// </summary>
    public class DoveadmClient : IDoveadmClient
    {
        private readonly HttpClient _http;
        private readonly DovecotOptions _options;
        private readonly ILogger<DoveadmClient> _logger;

        public DoveadmClient(HttpClient http, IOptions<DovecotOptions> options, ILogger<DoveadmClient> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public Task<Result<Quota>> GetQuotaAsync(User user, CancellationToken cancellationToken = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            return CallDoveadmAsync(command: "quotaGet", parameters: new { user = user.Email }, tag: "q1",
                logTarget: user.Email,
                notConfiguredMessage: "Quota service is not configured",
                failureMessage: "Unable to retrieve quota",
                ParseQuotaRows, cancellationToken);
        }

        public Task<Result<IReadOnlyList<string>>> GetMailboxesAsync(User user, CancellationToken cancellationToken = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            return CallDoveadmAsync(command: "mailboxList", parameters: new { user = user.Email }, tag: "m1",
                logTarget: user.Email,
                notConfiguredMessage: "Mailbox service is not configured",
                failureMessage: "Unable to retrieve mailboxes",
                ParseMailboxRows, cancellationToken);
        }

        public async Task<Result> FlushAuthCacheAsync(string email, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));
            return await FlushAuthCacheAsync(parameters: new { users = new[] { email } }, logTarget: email, cancellationToken);
        }

        public Task<Result> FlushAllAuthCacheAsync(CancellationToken cancellationToken = default) =>
            FlushAuthCacheAsync(parameters: new { }, logTarget: "*", cancellationToken);

        // doveadm "auth cache flush" returns a single { "count": N } row we don't need;
        // we only care whether the command succeeded.
        private async Task<Result> FlushAuthCacheAsync(object parameters, string logTarget, CancellationToken cancellationToken)
        {
            var result = await CallDoveadmAsync(command: "authCacheFlush", parameters, tag: "f1",
                logTarget: logTarget,
                notConfiguredMessage: "Auth cache service is not configured",
                failureMessage: "Unable to flush auth cache",
                parseRows: _ => Result.Success(true), cancellationToken);

            return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
        }

        /// <summary>
        /// Sends a single doveadm command and hands the response rows
        /// (the payload of the "doveadmResponse" envelope) to <paramref name="parseRows"/>.
        /// </summary>
        private async Task<Result<T>> CallDoveadmAsync<T>(
            string command,
            object parameters,
            string tag,
            string logTarget,
            string notConfiguredMessage,
            string failureMessage,
            Func<JsonElement, Result<T>> parseRows,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiUrl) || string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                _logger.LogError("Dovecot API is not configured (ApiUrl/ApiKey missing)");
                return Result.Failure<T>(notConfiguredMessage);
            }

            var payload = new object[]
            {
                new object[] { command, parameters, tag }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.ApiUrl)
            {
                Content = JsonContent.Create(payload)
            };
            var apiKeyB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(_options.ApiKey));
            request.Headers.TryAddWithoutValidation("Authorization", "X-Dovecot-API " + apiKeyB64);

            try
            {
                using var response = await _http.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Dovecot {Command} HTTP {Status} for target={Target}", command, (int)response.StatusCode, logTarget);
                    return Result.Failure<T>(failureMessage);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                var rows = ExtractDoveadmRows(doc.RootElement, command, logTarget, failureMessage);
                if (rows.IsFailure)
                    return Result.Failure<T>(rows.Error);

                return parseRows(rows.Value);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dovecot {Command} failed for target={Target}", command, logTarget);
                return Result.Failure<T>(failureMessage);
            }
        }

        /// <summary>
        /// Validates the doveadm response envelope <c>[["doveadmResponse", rows, tag]]</c>
        /// and returns the rows array.
        /// </summary>
        private Result<JsonElement> ExtractDoveadmRows(JsonElement root, string command, string logTarget, string failureMessage)
        {
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return Result.Failure<JsonElement>("Unexpected response from Dovecot");

            var first = root[0];
            if (first.ValueKind != JsonValueKind.Array || first.GetArrayLength() < 2)
                return Result.Failure<JsonElement>("Unexpected response from Dovecot");

            var kind = first[0].GetString();
            if (kind == "error")
            {
                _logger.LogWarning("Dovecot {Command} error for target={Target}: {Payload}", command, logTarget, first[1].GetRawText());
                return Result.Failure<JsonElement>(failureMessage);
            }

            if (kind != "doveadmResponse")
                return Result.Failure<JsonElement>("Unexpected response from Dovecot");

            var rows = first[1];
            if (rows.ValueKind != JsonValueKind.Array)
                return Result.Failure<JsonElement>("Unexpected response from Dovecot");

            return Result.Success(rows);
        }

        private static Result<IReadOnlyList<string>> ParseMailboxRows(JsonElement rows)
        {
            var mailboxes = new List<string>();
            foreach (var row in rows.EnumerateArray())
            {
                if (row.TryGetProperty("mailbox", out var mb))
                {
                    var name = mb.GetString();
                    if (!string.IsNullOrEmpty(name))
                        mailboxes.Add(name);
                }
            }

            return Result.Success<IReadOnlyList<string>>(mailboxes);
        }

        private static Result<Quota> ParseQuotaRows(JsonElement rows)
        {
            var quota = new Quota();
            foreach (var row in rows.EnumerateArray())
            {
                var type = row.TryGetProperty("type", out var t) ? t.GetString() : null;
                var value = ReadLong(row, "value");
                var limit = ReadLong(row, "limit");

                if (string.Equals(type, "STORAGE", StringComparison.OrdinalIgnoreCase))
                {
                    // Dovecot STORAGE quota is reported in kibibytes (1 unit = 1024 bytes)
                    quota.StorageBytesUsed = value * 1024;
                    quota.StorageBytesLimit = limit * 1024;
                }
                else if (string.Equals(type, "MESSAGE", StringComparison.OrdinalIgnoreCase))
                {
                    quota.MessageCount = value;
                    quota.MessageLimit = limit;
                }
            }

            return Result.Success(quota);
        }

        private static long ReadLong(JsonElement row, string name)
        {
            if (!row.TryGetProperty(name, out var elem))
                return 0;
            return elem.ValueKind switch
            {
                JsonValueKind.Number => elem.TryGetInt64(out var n) ? n : 0,
                JsonValueKind.String => long.TryParse(elem.GetString(), out var s) ? s : 0,
                _ => 0
            };
        }
    }
}
