using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>The SMTP account that sends a device's invitation mails, configured in Administration > Application
/// only. Admin-only; the password is write-only and never follows a host or port the request changes.</summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Policy = AdminRequirement.PolicyName)]
public sealed class SchedulingAccountController(
    ISchedulingAccountStore store,
    IServiceAccountSecretProtector protector,
    IServiceAccountProvider accounts,
    ISmtpConnectionFactory smtp,
    IOptionsMonitor<MailOptions> mailOptions,
    TimeProvider clock) : ApiBaseController
{
    internal const string NotConfigured = "No service account is configured";
    // Codes, not prose: the form itself can meet these refusals (a stale load, another admin's change).
    internal const string SecurityNoneDisallowed = "security_none_disallowed";
    internal const string PasswordRequired = ConnectedAccountErrors.PasswordRequired;
    internal const string PasswordUnreadable = SchedulingAccountTestErrors.PasswordUnreadable;
    internal const string PasswordRequiredForNewEndpoint = "password_required_for_new_endpoint";
    internal const string TestInProgress = "connection_test_in_progress";
    internal const string ChangedConcurrently = "scheduling_account_changed_concurrently";

    private const int MaxLoginLength = 320;

    internal static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private static readonly SchedulingAccountTestResult Unreadable = new(false, SchedulingAccountTestErrors.PasswordUnreadable);

    /// <summary>One connection test at a time in this process: the endpoint must not become a port scanner.</summary>
    private static readonly SemaphoreSlim TestGate = new(1, 1);

    /// <summary>The stored account, without its password, and whether an unencrypted endpoint may be saved.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The account; only the flags when none is stored</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SchedulingAccountResponse>> GetSchedulingAccount(CancellationToken cancellationToken)
    {
        var row = await store.FindAsync(cancellationToken);
        var allowCleartext = mailOptions.CurrentValue.AllowCleartext;
        return Ok(row is null
            ? SchedulingAccountResponse.None(allowCleartext)
            : new SchedulingAccountResponse(true, row.Host, row.Port, row.Security, row.Login,
                PasswordStored: true, PasswordReadable: protector.Unprotect(row.PasswordCipher) is not null, allowCleartext,
                row.LastTestAt is { } testedAt ? DateTime.SpecifyKind(testedAt, DateTimeKind.Utc) : null, row.LastTestOk));
    }

    /// <summary>Creates or replaces the account, forgetting its last test.</summary>
    /// <param name="request">the endpoint and login; an empty password keeps the stored one</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Account stored; the invitation queue uses it from now on</response>
    /// <response code="400">An invalid field (English prose); or one of the codes the form can reach:
    /// <c>security_none_disallowed</c> (None while Mail:AllowCleartext is off), and, with no password,
    /// <c>password_required</c> (none stored), <c>password_required_for_new_endpoint</c> (the host or port
    /// differs from the stored one), <c>password_unreadable</c> (the stored one no longer decrypts)</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    /// <response code="409">Concurrent writes won twice; or, for a save keeping the password, any other save or delete
    /// landed after the check (<c>scheduling_account_changed_concurrently</c>)</response>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> SaveSchedulingAccount(SchedulingAccountRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequestEnveloppe("Request body is required");

        if (Validate(request) is { } invalid) return BadRequestEnveloppe(invalid);

        var (host, security, login) = (request.Host!, request.Security!, request.Login!.Trim());
        bool saved;
        if (string.IsNullOrEmpty(request.Password))
        {
            var (_, checkedVersion, refusal) = await StoredPasswordForAsync(request, cancellationToken);
            if (refusal is not null) return BadRequestEnveloppe(refusal);
            saved = await store.SaveKeepingPasswordAsync(host, request.Port, security, login, checkedVersion, cancellationToken);
        }
        else
        {
            saved = await store.SaveAsync(host, request.Port, security, login, protector.Protect(request.Password), cancellationToken);
        }
        if (!saved) return ConflictEnveloppe(ChangedConcurrently);
        // Committed: an aborted request must not leave the cache unknown.
        await accounts.InvalidateAsync(CancellationToken.None);
        return NoContent();
    }

    /// <summary>Removes the account; device writes then send no invitation.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">No account stored, whether or not there was one</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    /// <response code="409">Concurrent saves won twice (<c>scheduling_account_changed_concurrently</c>)</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteSchedulingAccount(CancellationToken cancellationToken)
    {
        if (!await store.DeleteAsync(cancellationToken)) return ConflictEnveloppe(ChangedConcurrently);
        await accounts.InvalidateAsync(CancellationToken.None);
        return NoContent();
    }

    /// <summary>
    /// Connects and authenticates, then hangs up: no mail is ever sent. Bounded by <see cref="TestTimeout"/>.
    ///
    /// With a body, tests the values entered and records nothing; an empty password means the stored
    /// one, allowed only on the stored host and port. Without, tests the stored account and records
    /// the verdict on it, unless it was saved again while the test ran.
    ///
    /// <c>error</c> is one of: <c>smtp_unreachable</c> (no answer, connection refused, or any
    /// unclassified failure), <c>smtp_auth_failed</c> (the server refused the login or password),
    /// <c>smtp_auth_unsupported</c> (the server offers no authentication this service can use),
    /// <c>smtp_tls_failed</c> (no STARTTLS, a failed TLS handshake or certificate, or a connection left
    /// unencrypted while Mail:AllowCleartext is off), <c>smtp_timeout</c> (no complete answer in time),
    /// <c>password_unreadable</c> (the stored password no longer decrypts; nothing was attempted),
    /// <c>stored_account_invalid</c> (the stored security value is unknown; nothing was attempted).
    /// </summary>
    /// <param name="request">the values to test, or no body to test the stored account</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The verdict: <c>{ ok, error? }</c></response>
    /// <response code="400">As for a save: prose for an invalid field, or <c>security_none_disallowed</c>,
    /// <c>password_required</c>, <c>password_required_for_new_endpoint</c></response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    /// <response code="404">No body, and no account stored</response>
    /// <response code="409">Another connection test is running (<c>connection_test_in_progress</c>)</response>
    [HttpPost("Test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SchedulingAccountTestResult>> TestSchedulingAccount(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SchedulingAccountRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            var stored = await store.FindAsync(cancellationToken);
            if (stored is null) return NotFoundEnveloppe(NotConfigured);
            if (ServiceSmtpAccount.Open(stored, protector, out var readable) is not { } account)
                return Ok(readable ? new SchedulingAccountTestResult(false, SchedulingAccountTestErrors.StoredAccountInvalid) : Unreadable);

            if (await ConnectAsync(account, cancellationToken) is not { } verdict) return ConflictEnveloppe(TestInProgress);
            await store.RecordTestAsync(clock.GetUtcNow().UtcDateTime, verdict.Ok, stored.UpdatedAt, cancellationToken);
            return Ok(verdict);
        }

        if (Validate(request) is { } invalid) return BadRequestEnveloppe(invalid);

        var password = request.Password;
        if (string.IsNullOrEmpty(password))
        {
            (password, _, var refusal) = await StoredPasswordForAsync(request, cancellationToken);
            if (refusal is PasswordUnreadable) return Ok(Unreadable);
            if (refusal is not null) return BadRequestEnveloppe(refusal);
        }

        MailConnectionBuilder.TryParseSecurity(request.Security!, allowCleartext: true, out var security);
        var entered = new ServiceSmtpAccount(request.Host!, request.Port, security, request.Login!.Trim(), password!);
        return await ConnectAsync(entered, cancellationToken) is { } result ? Ok(result) : ConflictEnveloppe(TestInProgress);
    }

    /// <summary>The stored password for a request that left its own empty, with the version it was checked on, or
    /// why it cannot have it: nothing stored, another host or port (it never travels elsewhere), or undecryptable.</summary>
    private async Task<(string? Password, DateTime CheckedVersion, string? Refusal)> StoredPasswordForAsync(
        SchedulingAccountRequest request, CancellationToken cancellationToken)
    {
        if (await store.FindAsync(cancellationToken) is not { } row) return (null, default, PasswordRequired);
        if (!string.Equals(row.Host, request.Host, StringComparison.OrdinalIgnoreCase) || row.Port != request.Port)
            return (null, row.UpdatedAt, PasswordRequiredForNewEndpoint);
        return protector.Unprotect(row.PasswordCipher) is { } password
            ? (password, row.UpdatedAt, null)
            : (null, row.UpdatedAt, PasswordUnreadable);
    }

    /// <summary>Null, attempting nothing, while another test holds the gate.</summary>
    private async Task<SchedulingAccountTestResult?> ConnectAsync(ServiceSmtpAccount account, CancellationToken cancellationToken)
    {
        if (!TestGate.Wait(0)) return null;
        using var budget = new CancellationTokenSource(TestTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        try
        {
            var opened = await smtp.OpenAsync(account.ToConnection(), linked.Token);
            if (opened.IsFailure)
                return new SchedulingAccountTestResult(false, SchedulingAccountTestErrors.FromConnectionError(opened.Error));

            await opened.Value.DisposeAsync();
            return new SchedulingAccountTestResult(true);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new SchedulingAccountTestResult(false, SchedulingAccountTestErrors.SmtpTimeout);
        }
        finally
        {
            TestGate.Release();
        }
    }

    private string? Validate(SchedulingAccountRequest request)
    {
        if (MailEndpointRules.ValidateHost(request.Host) is { } hostError) return hostError;
        if (request.Port is < 1 or > 65535) return "Port must be between 1 and 65535";
        if (MailEndpointRules.ValidateSecurity(request.Security, mailOptions.CurrentValue.AllowCleartext) is { } securityError)
            return securityError == MailEndpointRules.CleartextRefused ? SecurityNoneDisallowed : $"Smtp {securityError}";
        if (string.IsNullOrWhiteSpace(request.Login) || request.Login.Trim().Length > MaxLoginLength)
            return $"Login must be between 1 and {MaxLoginLength} characters";
        return DataProtectionSecretProtector.ValidateLength(request.Password, "password");
    }
}
