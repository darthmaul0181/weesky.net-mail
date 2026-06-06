using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// Queries a remote Dovecot server via its doveadm HTTP API.
    /// See https://doc.dovecot.org/admin_manual/doveadm_http_api/
    /// </summary>
    public class DovecotQuotaClient : IDovecotQuotaClient
    {
        private readonly HttpClient _http;
        private readonly DovecotOptions _options;
        private readonly ILogger<DovecotQuotaClient> _logger;

        public DovecotQuotaClient(HttpClient http, IOptions<DovecotOptions> options, ILogger<DovecotQuotaClient> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<Result<Quota>> GetQuotaAsync(User user, CancellationToken cancellationToken = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            if (string.IsNullOrWhiteSpace(_options.ApiUrl) || string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                _logger.LogError("Dovecot API is not configured (ApiUrl/ApiKey missing)");
                return Result.Failure<Quota>("Quota service is not configured");
            }

            var payload = new object[]
            {
                new object[] { "quotaGet", new { user = user.Email }, "q1" }
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
                    _logger.LogWarning("Dovecot quotaGet HTTP {Status} for user={User}", (int)response.StatusCode, user.Email);
                    return Result.Failure<Quota>("Unable to retrieve quota");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                return ParseQuotaResponse(doc.RootElement, user);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dovecot quotaGet failed for user={User}", user.Email);
                return Result.Failure<Quota>("Unable to retrieve quota");
            }
        }

        public async Task<Result<IReadOnlyList<string>>> GetMailboxesAsync(User user, CancellationToken cancellationToken = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            if (string.IsNullOrWhiteSpace(_options.ApiUrl) || string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                _logger.LogError("Dovecot API is not configured (ApiUrl/ApiKey missing)");
                return Result.Failure<IReadOnlyList<string>>("Mailbox service is not configured");
            }

            var payload = new object[]
            {
                new object[] { "mailboxList", new { user = user.Email }, "m1" }
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
                    _logger.LogWarning("Dovecot mailboxList HTTP {Status} for user={User}", (int)response.StatusCode, user.Email);
                    return Result.Failure<IReadOnlyList<string>>("Unable to retrieve mailboxes");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                return ParseMailboxesResponse(doc.RootElement, user);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dovecot mailboxList failed for user={User}", user.Email);
                return Result.Failure<IReadOnlyList<string>>("Unable to retrieve mailboxes");
            }
        }

        private Result<IReadOnlyList<string>> ParseMailboxesResponse(JsonElement root, User user)
        {
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return Result.Failure<IReadOnlyList<string>>("Unexpected response from Dovecot");

            var first = root[0];
            if (first.ValueKind != JsonValueKind.Array || first.GetArrayLength() < 2)
                return Result.Failure<IReadOnlyList<string>>("Unexpected response from Dovecot");

            var kind = first[0].GetString();
            if (kind == "error")
            {
                _logger.LogWarning("Dovecot mailboxList error for user={User}: {Payload}", user.Email, first[1].GetRawText());
                return Result.Failure<IReadOnlyList<string>>("Unable to retrieve mailboxes");
            }

            if (kind != "doveadmResponse")
                return Result.Failure<IReadOnlyList<string>>("Unexpected response from Dovecot");

            var rows = first[1];
            if (rows.ValueKind != JsonValueKind.Array)
                return Result.Failure<IReadOnlyList<string>>("Unexpected response from Dovecot");

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

        private Result<Quota> ParseQuotaResponse(JsonElement root, User user)
        {
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return Result.Failure<Quota>("Unexpected response from Dovecot");

            var first = root[0];
            if (first.ValueKind != JsonValueKind.Array || first.GetArrayLength() < 2)
                return Result.Failure<Quota>("Unexpected response from Dovecot");

            var kind = first[0].GetString();
            if (kind == "error")
            {
                _logger.LogWarning("Dovecot quotaGet error for user={User}: {Payload}", user.Email, first[1].GetRawText());
                return Result.Failure<Quota>("Unable to retrieve quota");
            }

            if (kind != "doveadmResponse")
                return Result.Failure<Quota>("Unexpected response from Dovecot");

            var rows = first[1];
            if (rows.ValueKind != JsonValueKind.Array)
                return Result.Failure<Quota>("Unexpected response from Dovecot");

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
