using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Authentication.Extensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Controllers;

public abstract class ApiBaseController : ControllerBase
{
    /// <summary>
    /// The authenticated user (JWT or Cookie authenticated).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The principal carries no Upn/Dns claims. Unreachable behind <c>[Authorize]</c>, since the
    /// bearer handler fails such a token before an action runs — so it is a wiring mistake, and
    /// saying so beats the NullReferenceException the caller used to get one dereference later.
    /// </exception>
    public User AuthenticatedUser =>
        this.GetUser() ?? throw new InvalidOperationException(
            "No authenticated user on this request. An action reading AuthenticatedUser must be [Authorize]d.");

    protected ActionResult FromResult(Result result, int errorStatusCode = StatusCodes.Status400BadRequest, int successStatusCode = StatusCodes.Status200OK)
    {
        if (result.IsSuccess) return StatusCode(successStatusCode);
        return StatusCode(errorStatusCode, ResultEnveloppe.CreateErrorEnveloppe(result.Error));
    }

    protected ActionResult<T> FromResult<T>(Result<T> result, int errorStatusCode = StatusCodes.Status400BadRequest)
    {
        if (result.IsSuccess) return Ok(result.Value);
        return StatusCode(errorStatusCode, ResultEnveloppe.CreateErrorEnveloppe(result.Error));
    }

    // The error envelope is this API's response shape, so the wrapping belongs here rather than
    // at each of the ~90 call sites that spelled it out. Each helper keeps the framework's own
    // factory underneath — BadRequest, not StatusCode(400) — because the concrete result type is
    // what the controller tests assert on.

    /// <summary>400 carrying the error envelope.</summary>
    protected ActionResult BadRequestEnveloppe(string message) =>
        BadRequest(ResultEnveloppe.CreateErrorEnveloppe(message));

    /// <summary>401 carrying the error envelope.</summary>
    protected ActionResult UnauthorizedEnveloppe(string message) =>
        Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(message));

    /// <summary>404 carrying the error envelope.</summary>
    protected ActionResult NotFoundEnveloppe(string message) =>
        NotFound(ResultEnveloppe.CreateErrorEnveloppe(message));

    /// <summary>502 carrying the error envelope — anything the mail server refused.</summary>
    protected ActionResult BadGatewayEnveloppe(string message) =>
        StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(message));
}
