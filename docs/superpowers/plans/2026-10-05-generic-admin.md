# Administration sur la plateforme generic — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Proposer l'écran Administration en `generic` (Domaines externes + Application) et quel que soit le compte actif, avec des admins désignés par configuration.

**Architecture:** Un objet `GenericAdministrators`, lu et validé une fois au démarrage, est la seule source de « qui est admin » en `generic`. Un handler de policy et `ClaimsAccountInfoProvider` le consultent. Les quatre routes des domaines externes passent du provider weesky au cœur, à URL constante. Côté front, l'entrée de menu perd la condition `isPrimary`, la page filtre ses onglets selon `capabilities.platform`, et le gate attend les capabilities.

**Tech Stack:** ASP.NET Core (.NET, xUnit, Moq), React + TypeScript (Vitest, Testing Library).

**Spec:** `docs/superpowers/2026-10-05-generic-admin-design.md`

## Global Constraints

- Réglage : clé `Generic:Administrators` (variable d'environnement `Generic__Administrators`), une chaîne d'adresses séparées par des virgules.
- Comparaison insensible à la casse, sur `Upn@Dns` (l'adresse de connexion).
- Une entrée invalide empêche le démarrage (`InvalidOperationException`). Le message contient `Generic:Administrators` et l'entrée fautive.
- Réglage absent ou vide : personne n'est admin, sans erreur.
- En `weesky`, le réglage n'est pas lu.
- URL des domaines externes inchangées : `api/Admin/domains/external` et `api/Admin/domains/external/{id:guid}`.
- Front : les gates lisent `!== false` / `!== 'generic'`. Absent ou null signifie « version weesky ».
- Commentaires : seulement quand le code ne s'explique pas, 3 lignes max. Pas de duplication.
- Tests backend : `dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés). Avant chaque commit, `git checkout -- '**/ApiDocumentation.xml'` si `git status` le montre modifié sans raison.
- Le dépôt est en `autocrlf=true` : pour un remplacement multi-lignes dans un fichier existant, préférer un script python plutôt que l'outil Edit, et vérifier `git diff` (aucune ligne modifiée hors du changement voulu).
- Messages de commit : deux lignes max, via heredoc `git commit -F - <<'EOF'`, terminés par `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Ne jamais pousser.

## Review Focus

1. Une adresse admin avec une casse différente de la saisie au login (`Michael@Exemple.be` vs `michael@exemple.be`) : l'utilisateur est admin. Test dans la tâche 1.
2. Une liste avec espaces et virgule finale (`" a@x.be , b@y.be ,"`) : deux admins, aucune erreur. Test dans la tâche 1.
3. Rechargement de `/settings/admin` en `generic` : aucun appel à `api/Admin/users`, aucune erreur affichée. Test dans la tâche 3 (gate en `'wait'`, puis `adminGetUsers` jamais appelé en `generic`).
4. Un non-admin `generic` qui appelle une route d'administration (domaines externes, logo) : 403, pas 404 ni 500. Test dans la tâche 2.
5. Un admin weesky sur un compte connecté actif : l'entrée Administration reste, et les cinq onglets aussi (la plateforme est weesky). Test dans la tâche 3.

---

### Task 1: Admins generic désignés par configuration

**Files:**
- Create: `src/scotty.microservice/Platform/Generic/GenericAdministrators.cs`
- Create: `src/scotty.microservice/Platform/Generic/GenericAdminRequirementHandler.cs`
- Modify: `src/scotty.microservice/Platform/Generic/ClaimsAccountInfoProvider.cs`
- Modify: `src/scotty.microservice/Configuration/PlatformConfiguration.cs` (`AddGenericPlatform`)
- Modify: `src/scotty.microservice.host/Program.cs:28`
- Modify: `src/scotty.microservice/Controllers/CapabilitiesController.cs:57`
- Modify: `src/scotty.microservice/Authentication/Authorization/AdminRequirement.cs` (summary : le handler generic existe désormais)
- Test: `src/scotty.microservice/scotty.microservice.Tests/Platform/GenericAdministratorsTests.cs` (create)
- Test: `src/scotty.microservice/scotty.microservice.Tests/Platform/GenericAdminRequirementHandlerTests.cs` (create)
- Test: `src/scotty.microservice/scotty.microservice.Tests/Platform/ClaimsAccountInfoProviderTests.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Configuration/PlatformConfigurationTests.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Configuration/PlatformBootTests.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Controllers/CapabilitiesControllerTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class GenericAdministrators` (namespace `weesky.Scotty.Microservice.Platform.Generic`)
    - `public const string ConfigurationKey = "Generic:Administrators";`
    - `public GenericAdministrators(IEnumerable<string> emails)`
    - `public bool Contains(string email)`
    - `public static GenericAdministrators From(IConfiguration configuration)`
  - `internal sealed class GenericAdminRequirementHandler(GenericAdministrators administrators) : AuthorizationHandler<AdminRequirement>`
  - `public static IServiceCollection AddGenericPlatform(this IServiceCollection services, IConfiguration configuration)` (nouvelle signature)
  - `ClaimsAccountInfoProvider(GenericAdministrators administrators)`

- [ ] **Step 1: Write the failing tests for `GenericAdministrators`**

```csharp
using Microsoft.Extensions.Configuration;
using weesky.Scotty.Microservice.Platform.Generic;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Platform;

public sealed class GenericAdministratorsTests
{
    private static GenericAdministrators From(string? value) =>
        GenericAdministrators.From(new ConfigurationBuilder()
            .AddInMemoryCollection([new(GenericAdministrators.ConfigurationKey, value)])
            .Build());

    [Fact]
    public void From_ReadsEveryAddress_TrimmingSpacesAndIgnoringEmptyEntries()
    {
        var admins = From(" anne@exemple.be , michael@exemple.be ,");

        Assert.True(admins.Contains("anne@exemple.be"));
        Assert.True(admins.Contains("michael@exemple.be"));
        Assert.False(admins.Contains(""));
    }

    [Fact]
    public void Contains_IgnoresCase() =>
        Assert.True(From("Michael@Exemple.be").Contains("michael@exemple.BE"));

    [Fact]
    public void Contains_IsAnExactMatch_NotASuffixOrPrefix()
    {
        var admins = From("michael@exemple.be");

        Assert.False(admins.Contains("xmichael@exemple.be"));
        Assert.False(admins.Contains("michael@exemple.be.evil"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void From_WithNothingConfigured_HasNoAdministrator(string? value) =>
        Assert.False(From(value).Contains("michael@exemple.be"));

    [Theory]
    [InlineData("michael")]
    [InlineData("@exemple.be")]
    [InlineData("michael@")]
    [InlineData("a@b@c")]
    public void From_WithAnInvalidEntry_RefusesToStartNamingTheKeyAndTheEntry(string entry)
    {
        var error = Assert.Throws<InvalidOperationException>(() => From($"anne@exemple.be,{entry}"));

        Assert.Contains(GenericAdministrators.ConfigurationKey, error.Message);
        Assert.Contains($"'{entry}'", error.Message);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter FullyQualifiedName~GenericAdministratorsTests`
Expected: build error, `GenericAdministrators` does not exist.

- [ ] **Step 3: Implement `GenericAdministrators`**

```csharp
using Microsoft.Extensions.Configuration;

namespace weesky.Scotty.Microservice.Platform.Generic;

/// <summary>
/// Who administers a generic deployment: no directory holds an admin flag, so the operator names
/// the addresses. Read once at startup; a misspelt entry refuses to start rather than silently
/// granting nobody.
/// </summary>
public sealed class GenericAdministrators(IEnumerable<string> emails)
{
    public const string ConfigurationKey = "Generic:Administrators";

    private readonly HashSet<string> _emails = new(emails, StringComparer.OrdinalIgnoreCase);

    public bool Contains(string email) => _emails.Contains(email);

    public static GenericAdministrators From(IConfiguration configuration)
    {
        var entries = (configuration[ConfigurationKey] ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
            if (entry.Split('@') is not [{ Length: > 0 }, { Length: > 0 }])
                throw new InvalidOperationException(
                    $"'{ConfigurationKey}' holds '{entry}', which is not an email address.");

        return new GenericAdministrators(entries);
    }
}
```

- [ ] **Step 4: Run them to verify they pass**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter FullyQualifiedName~GenericAdministratorsTests`
Expected: PASS.

- [ ] **Step 5: Write the failing handler tests**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using weesky.Scotty.Microservice.Authentication.Authorization;
using weesky.Scotty.Microservice.Platform.Generic;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Platform;

public sealed class GenericAdminRequirementHandlerTests
{
    private static async Task<bool> SucceedsAsync(params Claim[] claims)
    {
        var requirement = new AdminRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement], new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), resource: null);

        await new GenericAdminRequirementHandler(new GenericAdministrators(["michael@exemple.be"]))
            .HandleAsync(context);
        return context.HasSucceeded;
    }

    private static Claim[] Login(string name, string domain) =>
        [new(ClaimTypes.Upn, name), new(ClaimTypes.Dns, domain)];

    [Fact]
    public async Task AListedAddress_IsGranted() => Assert.True(await SucceedsAsync(Login("michael", "exemple.be")));

    [Fact]
    public async Task AListedAddress_InAnotherCase_IsGranted() =>
        Assert.True(await SucceedsAsync(Login("Michael", "EXEMPLE.be")));

    [Fact]
    public async Task AnUnlistedAddress_IsRefused() => Assert.False(await SucceedsAsync(Login("anne", "exemple.be")));

    [Fact]
    public async Task APrincipalWithoutLoginClaims_IsRefused() => Assert.False(await SucceedsAsync());
}
```

- [ ] **Step 6: Run them to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter FullyQualifiedName~GenericAdminRequirementHandlerTests`
Expected: build error, `GenericAdminRequirementHandler` does not exist.

- [ ] **Step 7: Implement the handler**

The test project already sees `internal` types (the existing `ClaimsAccountInfoProvider` is internal and tested). Check with `grep -rn InternalsVisibleTo src/scotty.microservice`. If it is not visible, make the class `public`.

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using weesky.Scotty.Microservice.Authentication.Authorization;

namespace weesky.Scotty.Microservice.Platform.Generic;

internal sealed class GenericAdminRequirementHandler(GenericAdministrators administrators)
    : AuthorizationHandler<AdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        var name = context.User.FindFirst(ClaimTypes.Upn)?.Value;
        var domain = context.User.FindFirst(ClaimTypes.Dns)?.Value;

        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(domain) &&
            administrators.Contains($"{name}@{domain}"))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 8: `ClaimsAccountInfoProvider` follows the list. Write the failing test**

In `ClaimsAccountInfoProviderTests.cs`, replace every `new ClaimsAccountInfoProvider()` with `Provider()` and add:

```csharp
    private static ClaimsAccountInfoProvider Provider(params string[] admins) => new(new GenericAdministrators(admins));

    [Fact]
    public async Task GetAccountInfoAsync_IsAdminFollowsTheConfiguredList()
    {
        var provider = Provider("mick@weesky.be");

        Assert.True((await provider.GetAccountInfoAsync(new User("Mick@weesky.be"), CancellationToken.None)).Value.IsAdmin);
        Assert.False((await provider.GetAccountInfoAsync(new User("anne@weesky.be"), CancellationToken.None)).Value.IsAdmin);
    }
```

In `GetAccountInfoAsync_CarriesNoDirectoryFacts`, the `Assert.False(result.Value.IsAdmin)` stays valid (empty list). Change its summary to `/// <summary>Nothing behind the token: no directory row, no numeric id; admin only by the configured list.</summary>`.

- [ ] **Step 9: Implement it**

In `ClaimsAccountInfoProvider.cs`: primary constructor `internal sealed class ClaimsAccountInfoProvider(GenericAdministrators administrators) : IAccountInfoProvider`, and `IsAdmin = administrators.Contains(user.Email),`. In the class summary, replace "owned domains or an admin flag from" with "or owned domains from; the admin flag comes from <see cref=\"GenericAdministrators\"/>".

- [ ] **Step 10: `AddGenericPlatform` takes the configuration. Update the tests first**

`PlatformConfigurationTests.AddGenericPlatform_RegistersTheClaimsOnlyAdapter`: call `new ServiceCollection().AddGenericPlatform(Configuration())`. Add:

```csharp
    [Fact]
    public void AddGenericPlatform_RegistersTheAdminHandler()
    {
        var services = new ServiceCollection().AddGenericPlatform(Configuration());

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IAuthorizationHandler));
        Assert.Equal(typeof(GenericAdminRequirementHandler), descriptor.ImplementationType);
    }

    [Fact]
    public void AddGenericPlatform_WithAnInvalidAdministrator_RefusesToStart() =>
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddGenericPlatform(Configuration((GenericAdministrators.ConfigurationKey, "michael"))));
```

(add `using Microsoft.AspNetCore.Authorization;`)

`PlatformBootTests.Compose`: `services.AddGenericPlatform(configuration);`

- [ ] **Step 11: Implement it**

```csharp
    public static IServiceCollection AddGenericPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(GenericAdministrators.From(configuration));
        services.AddSingleton<IAuthorizationHandler, GenericAdminRequirementHandler>();
        services.AddSingleton<IAliasDirectory, FreeIdentityDirectory>();
        services.AddSingleton<IProfileReader, NullProfileReader>();
        services.AddSingleton<IAccountInfoProvider, ClaimsAccountInfoProvider>();

        return services;
    }
```

Update its summary: "the three ports answer from the token and from nothing else; admin rights from the configured list." `Program.cs:28`: `else builder.Services.AddGenericPlatform(builder.Configuration);`.

In `AdminRequirement.cs`, replace the summary's second sentence with: "Each platform registers the handler that can satisfy it: <c>AdminRequirementHandler</c> reads the weesky directory, <c>GenericAdminRequirementHandler</c> the configured list."

- [ ] **Step 12: Capabilities report the admin flag on both platforms. Update the test first**

In `CapabilitiesControllerTests`, `GetCapabilities_OnGeneric_ReturnsThePlatformWiredFlagsFalse`: remove `Assert.False(capabilities.Admin);` and the final `_accountInfo.Verify(... Times.Never)`. Add:

```csharp
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetCapabilities_OnGeneric_AdminFollowsTheAccount(bool isAdmin)
    {
        _platform = new PlatformOptions { Platform = PlatformOptions.Generic };
        _accountInfo.Setup(a => a.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success(new AccountInfo { UserId = 0, UserName = "john", IsAdmin = isAdmin }));

        Assert.Equal(isAdmin, (await GetCapabilitiesAsync()).Admin);
    }
```

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter FullyQualifiedName~CapabilitiesControllerTests`
Expected: FAIL for `isAdmin: true`.

- [ ] **Step 13: Implement it**

`CapabilitiesController.cs:57`: `Admin: await IsAdminAsync(cancellationToken),`.

- [ ] **Step 14: Run the whole core suite**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests` then `dotnet test src/scotty.providers.weesky.Tests`
Expected: PASS. Then fix any remaining `new ClaimsAccountInfoProvider()` or `AddGenericPlatform()` call found by `grep -rn "new ClaimsAccountInfoProvider()\|AddGenericPlatform()" src`.

- [ ] **Step 15: Commit**

```bash
git add -A src/scotty.microservice src/scotty.microservice.host
git commit -F - <<'EOF'
Let a generic deployment name its administrators in its settings

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Les routes des domaines externes passent dans le cœur

**Files:**
- Create: `src/scotty.microservice/Controllers/ExternalDomainsController.cs`
- Modify: `src/scotty.providers.weesky/Controllers/AdminController.cs` (retirer les lignes 245-441 : les 4 routes et les helpers `Describe`, `ToEntity`, `ProtectedSecret`, `ParsedAuthMode`, `Validate`, `ValidateOAuth`, plus les champs et paramètres `_externalDomains`, `_secretProtector`, `_mailOptions`)
- Create: `src/scotty.microservice/scotty.microservice.Tests/Controllers/ExternalDomainsControllerTests.cs`
- Create: `src/scotty.microservice/scotty.microservice.Tests/Controllers/GenericAdminAuthorizationTests.cs`
- Modify: `src/scotty.providers.weesky.Tests/Controllers/AdminControllerTests.cs` (retirer les sections `GetExternalDomains` à `DeleteExternalDomain`, lignes 397-896, et les helpers/fixtures qui ne servent qu'à elles)

**Interfaces:**
- Consumes: `AddGenericPlatform(IServiceCollection, IConfiguration)`, `GenericAdministrators.ConfigurationKey` (tâche 1).
- Produces:
  - `public sealed class ExternalDomainsController(IExternalDomainStore externalDomains, IClientSecretProtector secretProtector, IOptionsMonitor<MailOptions> mailOptions) : ApiBaseController`
  - `[Route("api/Admin/domains/external")]`, `[ApiController]`, `[Authorize(Policy = AdminRequirement.PolicyName)]`
  - Actions, mêmes noms, mêmes signatures, mêmes réponses : `GetExternalDomains` `[HttpGet]`, `CreateExternalDomain` `[HttpPost]`, `UpdateExternalDomain` `[HttpPut("{id:guid}")]`, `DeleteExternalDomain` `[HttpDelete("{id:guid}")]`.

- [ ] **Step 1: Move the tests first**

Create `ExternalDomainsControllerTests.cs` in namespace `weesky.Scotty.Microservice.Tests.Controllers`. Move, unchanged, every test of `AdminControllerTests.cs` from `// ── GetExternalDomains` (line 397) to the end of `// ── DeleteExternalDomain`, plus the fixtures they use: `_externalDomains`, `_protector`, the constructor's `_protector.Setup`, `Domain(...)`, `ValidRequest(...)`, `OAuthRequest(...)`. Their factory:

```csharp
    private ExternalDomainsController CreateController(bool allowCleartext = false)
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new MailOptions { AllowCleartext = allowCleartext });

        var controller = new ExternalDomainsController(_externalDomains.Object, _protector.Object, monitor.Object);
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");
        return controller;
    }

    [Fact]
    public void Controller_IsProtectedByAdminPolicy()
    {
        var attribute = typeof(ExternalDomainsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AdminRequirement.PolicyName, attribute.Policy);
    }

    [Theory]
    [InlineData(nameof(ExternalDomainsController.GetExternalDomains), "GET", null)]
    [InlineData(nameof(ExternalDomainsController.CreateExternalDomain), "POST", null)]
    [InlineData(nameof(ExternalDomainsController.UpdateExternalDomain), "PUT", "{id:guid}")]
    [InlineData(nameof(ExternalDomainsController.DeleteExternalDomain), "DELETE", "{id:guid}")]
    public void TheRoutesKeepTheirAddress(string action, string verb, string? template)
    {
        Assert.Equal("api/Admin/domains/external",
            typeof(ExternalDomainsController).GetCustomAttribute<RouteAttribute>()!.Template);
        var http = typeof(ExternalDomainsController).GetMethod(action)!.GetCustomAttribute<HttpMethodAttribute>()!;
        Assert.Equal(verb, Assert.Single(http.HttpMethods));
        Assert.Equal(template, http.Template);
    }
```

(`using System.Reflection; using Microsoft.AspNetCore.Mvc.Routing;`.) Remove them from `AdminControllerTests.cs`. The `CreateController` that stays there builds `new AdminController(_repo.Object, _dovecot.Object)`. Keep any helper still used by a remaining weesky test. `grep` each helper name before deleting it.

- [ ] **Step 2: Run to verify the core tests fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter FullyQualifiedName~ExternalDomainsControllerTests`
Expected: build error, `ExternalDomainsController` does not exist.

- [ ] **Step 3: Move the code**

Create `ExternalDomainsController.cs` (namespace `weesky.Scotty.Microservice.Controllers`). Its body is the code cut from `AdminController.cs` lines 245-441, unchanged except `_externalDomains` → `externalDomains`, `_secretProtector` → `secretProtector`, `_mailOptions` → `mailOptions` (primary constructor), and the four route templates: `[HttpGet]`, `[HttpPost]`, `[HttpPut("{id:guid}")]`, `[HttpDelete("{id:guid}")]`. Keep every XML doc and `ProducesResponseType`. Class header:

```csharp
/// <summary>The admin-curated external providers users connect from. Core, not platform: the rows
/// live in the webmail's own database, so a generic deployment administers them too.</summary>
[Route("api/Admin/domains/external")]
[ApiController]
[Authorize(Policy = AdminRequirement.PolicyName)]
public sealed class ExternalDomainsController(
    IExternalDomainStore externalDomains,
    IClientSecretProtector secretProtector,
    IOptionsMonitor<MailOptions> mailOptions) : ApiBaseController
```

In `AdminController.cs`: delete the moved code, the three fields and constructor parameters, and the `using` that become unused (`Microsoft.Extensions.Options`, `weesky.Scotty.Microservice.Data.Preferences`, `weesky.Scotty.Microservice.Models.Mail`, `weesky.Scotty.Microservice.Repositories` if nothing else uses them; the build with warnings-as-errors, if enabled, says so).

- [ ] **Step 4: Run both suites**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests` and `dotnet test src/scotty.providers.weesky.Tests`
Expected: PASS, and the external-domain test count equals the one before the move. Compare with `git stash`-free counting: `grep -c "\[Fact\]\|\[Theory\]"` on the removed range vs the new file, minus the two new tests.

- [ ] **Step 5: Write the end-to-end generic authorization test**

Model: `SchedulingAccountAuthorizationTests.cs`. The difference: the admin handler is **the real one**, registered by `AddGenericPlatform`, and the test scheme issues `Upn`/`Dns` claims.

```csharp
/// <summary>
/// A generic deployment routed for real: the admin policy is answered by the handler AddGenericPlatform
/// registers from Generic:Administrators, not by a stand-in. Covers a moved route and one that never moved.
/// </summary>
public sealed class GenericAdminAuthorizationTests : IAsyncLifetime
{
    private const string UserHeader = "X-Test-User";
    private readonly string _database = Guid.NewGuid().ToString("N");
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new(GenericAdministrators.ConfigurationKey, "michael@exemple.be")])
            .Build();

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddControllers()
                        .AddJsonOptions(MvcFormatterConfiguration.ConfigureJson)
                        .ConfigureApplicationPartManager(manager =>
                        {
                            manager.ApplicationParts.Clear();
                            manager.ApplicationParts.Add(new AssemblyPart(typeof(ExternalDomainsController).Assembly));
                            manager.FeatureProviders.Clear();
                            manager.FeatureProviders.Add(new OnlyControllers());
                        });
                    services.AddScottyAuthentication()
                        .AddAuthentication()
                        .AddScheme<AuthenticationSchemeOptions, UserHandler>(UserHandler.SchemeName, _ => { });
                    services.Configure<AuthenticationOptions>(options =>
                        (options.DefaultAuthenticateScheme, options.DefaultChallengeScheme) = (UserHandler.SchemeName, UserHandler.SchemeName));
                    services.AddGenericPlatform(configuration);

                    services.AddScoped<PreferencesDbContext>(_ => new PreferencesTestDbContext(_database));
                    services.AddScoped<IExternalDomainStore, ExternalDomainStore>();
                    services.AddSingleton(Mock.Of<IClientSecretProtector>());
                    services.AddSingleton(Mock.Of<IOptionsMonitor<MailOptions>>(m => m.CurrentValue == new MailOptions()));
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private Task<HttpResponseMessage> GetAsync(string? user)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/Admin/domains/external");
        if (user is not null) request.Headers.Add(UserHeader, user);
        return _client.SendAsync(request);
    }

    [Fact]
    public async Task AConfiguredAdministrator_ListsTheExternalDomains()
    {
        using var response = await GetAsync("Michael@exemple.be");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnotherUser_Gets403() =>
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("anne@exemple.be")).StatusCode);

    [Fact]
    public async Task AnAnonymousCaller_Gets401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(null)).StatusCode);

    private sealed class OnlyControllers : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(ExternalDomainsController);
    }

    private sealed class UserHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string SchemeName = "TestUser";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user)) return Task.FromResult(AuthenticateResult.NoResult());

            var parts = user.ToString().Split('@');
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Upn, parts[0]), new Claim(ClaimTypes.Dns, parts[1])], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
```

Usings: those of `SchedulingAccountAuthorizationTests.cs`, plus `Microsoft.Extensions.Configuration` and `weesky.Scotty.Microservice.Platform.Generic`. If `AddGenericPlatform`'s singletons need a service this host lacks, the start fails with the name of that service: register a `Mock.Of<>` for it, never remove a registration from `AddGenericPlatform`.

- [ ] **Step 6: Run it**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter FullyQualifiedName~GenericAdminAuthorizationTests`
Expected: PASS (3 tests). To check that the test really tests something, temporarily change the configured address to `x@exemple.be`. The first test must then turn red (403). Put it back afterwards.

- [ ] **Step 7: Run everything and commit**

Run: `dotnet test src/scotty.microservice.sln`
Expected: PASS.

```bash
git checkout -- '**/ApiDocumentation.xml' 2>/dev/null; git status --short
git add -A src
git commit -F - <<'EOF'
Serve the external domain routes from the core, for both platforms

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

(Only revert `ApiDocumentation.xml` if its diff is unrelated drift. If the move changed the XML docs of these routes, keep those lines: check with `git diff` before reverting.)

---

### Task 3: L'Administration côté front

**Files:**
- Modify: `src/frontend/src/contexts/AuthContext.tsx` (`capabilitiesLoaded`)
- Modify: `src/frontend/src/layouts/gates.ts` (`allowAdmin`)
- Modify: `src/frontend/src/modules/settings/SettingsLayout.tsx:66`
- Modify: `src/frontend/src/modules/settings/admin/AdminPage.tsx`
- Test: `src/frontend/src/layouts/gates.test.ts`
- Test: `src/frontend/src/modules/settings/SettingsLayout.test.tsx`
- Test: `src/frontend/src/modules/settings/admin/AdminPage.test.tsx`
- Test: every test that builds a full `AuthContextValue` or mocks `useAuth` for a component touched here (`grep -rln "accountLoaded" src/frontend/src --include=*.test.*`)

**Interfaces:**
- Consumes: `GET /api/Capabilities` answers `admin: true` for a generic admin and `platform: 'generic'` (task 1).
- Produces: `AuthContextValue.capabilitiesLoaded: boolean`.

- [ ] **Step 1: Gate tests first**

In `gates.test.ts`, add `capabilitiesLoaded: true,` to the `auth()` defaults and add to `describe('allowAdmin')`:

```ts
  it('waits for the capabilities before letting an admin through', () => {
    expect(allowAdmin(auth({ isAdmin: true, capabilitiesLoaded: false }))).toBe('wait')
  })

  it('refuses a non-admin without waiting for the capabilities', () => {
    expect(allowAdmin(auth({ isAdmin: false, capabilitiesLoaded: false }))).toBe(false)
  })
```

Run: `npx vitest run src/layouts/gates.test.ts` (from `src/frontend`)
Expected: FAIL (type error on `capabilitiesLoaded`, then 'wait' expected).

- [ ] **Step 2: Implement `capabilitiesLoaded` and the gate**

`AuthContext.tsx`:
- interface: under `capabilities`, add `/** The capabilities call has answered, success or failure: until then a platform-dependent screen cannot pick its variant. */ capabilitiesLoaded: boolean`
- state: `const [capabilitiesLoaded, setCapabilitiesLoaded] = useState(false)`
- in `refreshAccount`: `if (current()) { setCapabilities(caps); setCapabilitiesLoaded(true) }`
- in the logged-out branch of the effect, next to `setCapabilities(null)`: `setCapabilitiesLoaded(false)`
- in the Provider value: `capabilitiesLoaded,`

`gates.ts`:

```ts
// Waits on the capabilities too: the page's tabs depend on the platform, and a generic deployment
// must never mount a weesky-only tab whose route it does not serve.
export function allowAdmin({ isAdmin, accountLoaded, capabilities, capabilitiesLoaded }: AuthContextValue): boolean | 'wait' {
  if (!accountLoaded) return 'wait' // account still loading — decide once known
  if (!isAdmin) return false
  if (!capabilitiesLoaded) return 'wait'
  return capabilities?.admin !== false
}
```

(Replace the existing comment above `allowAdmin`: its point, `!== false`, is kept in this new form only if a line is still needed; 3 lines max.)

Run: `npx vitest run src/layouts/gates.test.ts`
Expected: PASS.

- [ ] **Step 3: SettingsLayout test first**

In `SettingsLayout.test.tsx`, rewrite the test `'hides Account, Aliases and Administration for a connected account, keeps Identities and Rules'`:

```ts
  it('hides Account and Aliases for a connected account, keeps Administration, Identities and Rules', async () => {
    localStorage.setItem('mail.activeAccount', 'g1')
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: true })
    mocks.getConnectedAccounts.mockResolvedValue([connectedRow()])
    renderAt('/settings/mail')
    const nav = within(await screen.findByRole('navigation', { name: 'Settings' }))
    await waitFor(() => expect(nav.queryByText('Account')).not.toBeInTheDocument())
    expect(nav.queryByText('Aliases')).not.toBeInTheDocument()
    expect(nav.getByText('Administration')).toBeInTheDocument()
    expect(nav.getByText('Identities')).toBeInTheDocument()
    expect(nav.getByText('Rules')).toBeInTheDocument()
  })
```

Run: `npx vitest run src/modules/settings/SettingsLayout.test.tsx`
Expected: FAIL on Administration.

- [ ] **Step 4: Implement it**

`SettingsLayout.tsx:66`: `...(isAdmin && adminAvailable ? [...] : []),` (drop `isPrimary &&`). Add the reason above it, one line: `// Not gated isPrimary: administration is the deployment's, whichever mailbox is being read.`

Run: `npx vitest run src/modules/settings/SettingsLayout.test.tsx`
Expected: PASS. If a test reaching `/settings/admin` now hangs on 'wait', its `getCapabilities` mock must resolve (it already defaults to a value in `beforeEach`; check).

- [ ] **Step 5: AdminPage tests first**

`AdminPage` will read `useAuth()`. In `AdminPage.test.tsx`, add, next to the `api.js` mock:

```ts
const auth = vi.hoisted(() => ({ capabilities: null as { platform?: 'weesky' | 'generic' } | null }))
vi.mock('../../../contexts/AuthContext', () => ({ useAuth: () => auth }))
```

and in the existing `beforeEach` (create one at the top of the file's main `describe` if none): `auth.capabilities = null`. Then add:

```ts
describe('AdminPage tabs by platform', () => {
  const tabNames = () => within(screen.getByRole('navigation')).getAllByRole('button').map(b => b.textContent)

  it.each([null, { platform: 'weesky' as const }])('shows the five tabs on weesky (capabilities %o), Accounts open', async caps => {
    auth.capabilities = caps
    mocks.adminGetUsers.mockResolvedValue([])
    renderAdminPage()
    expect(tabNames()).toEqual(['Accounts', 'Domains', 'Virtual domains', 'External domains', 'Application'])
    await waitFor(() => expect(mocks.adminGetUsers).toHaveBeenCalled())
  })

  it('shows only External domains and Application on generic, External domains open', async () => {
    auth.capabilities = { platform: 'generic' }
    mocks.adminGetExternalDomains.mockResolvedValue([])
    renderAdminPage()
    expect(tabNames()).toEqual(['External domains', 'Application'])
    await waitFor(() => expect(mocks.adminGetExternalDomains).toHaveBeenCalled())
    expect(mocks.adminGetUsers).not.toHaveBeenCalled()
  })

  it('falls back to the first visible tab when the open one disappears', async () => {
    mocks.adminGetUsers.mockResolvedValue([])
    mocks.adminGetExternalDomains.mockResolvedValue([])
    const { rerender } = renderAdminPage()
    auth.capabilities = { platform: 'generic' }
    rerender(<AdminPage />)
    expect(tabNames()).toEqual(['External domains', 'Application'])
    expect(screen.getByRole('button', { name: 'External domains' })).toHaveClass('is-active')
  })
})
```

Adjust `within(screen.getByRole('navigation'))` if the page holds more than one `nav` (the tab bar is `<nav className="admin-tab-bar">`). Then use `container.querySelector('.admin-tab-bar')`. Also check that every test that mocks `useAuth` and renders `AdminPage` (`a11y.test.tsx`) still passes: a `useAuth` without `capabilities` reads as weesky.

Run: `npx vitest run src/modules/settings/admin/AdminPage.test.tsx`
Expected: FAIL on the generic tests.

- [ ] **Step 6: Implement it**

`AdminPage.tsx`:

```tsx
import { useAuth } from '../../../contexts/AuthContext'

const TABS = ['accounts', 'domains', 'virtualdomains', 'externaldomains', 'application'] as const
type Tab = typeof TABS[number]
/** Backed by the weesky directory: a generic deployment serves none of their routes. */
const DIRECTORY_TABS: readonly Tab[] = ['accounts', 'domains', 'virtualdomains']
```

In the component:

```tsx
  const { capabilities } = useAuth()
  const tabs = capabilities?.platform === 'generic' ? TABS.filter(tab => !DIRECTORY_TABS.includes(tab)) : TABS
  const [chosenTab, setActiveTab] = useState<Tab>(tabs[0])
  const activeTab = tabs.includes(chosenTab) ? chosenTab : tabs[0]
```

and `{tabs.map(tab => ...)}` in the tab bar. The rest is unchanged (`activeTab` stays the name read below).

Run: `npx vitest run src/modules/settings/admin/AdminPage.test.tsx`
Expected: PASS.

- [ ] **Step 7: Full front check and commit**

Run (from `src/frontend`): `npm run typecheck && npm run lint && npm test`
Expected: all green. Any test that builds a full `AuthContextValue` fails `typecheck` without `capabilitiesLoaded`: add `capabilitiesLoaded: true` there.

```bash
git add -A src/frontend
git commit -F - <<'EOF'
Offer Administration whichever account is active, its tabs following the platform

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 8: Browser check (local, both platforms)**

Only if a local stack is available: `Platform=generic` with `Generic__Administrators` set to your login address. Then `/settings/admin` after a hard reload: two tabs, no error toast, and no request to `api/Admin/users` in the network panel. Then the same with a connected account active. Report what was seen, or say this step was not run.

---

### Task 4: Documentation

**Files:**
- Modify: `install/scotty.microservice.env`
- Modify: `install/README.md` (§ "Two platforms" table, step 3.3 table, § "Features of the weesky platform")
- Modify: `README.md` (§ `generic`)

Public docs: one installation, no prod/dev or owner's servers, verifiable steps, plain words.

- [ ] **Step 1: The env file**

After the `Sieve__Host` line, in the CHANGE block, add:

```ini

# --- CHANGE: who administers Scotty: their mail addresses, separated by commas -------------------
# They see Settings › Administration: the product name and logo, external mail providers, the
# calendar service account. Leave it empty for no administrator.
Generic__Administrators=you@example.net
```

- [ ] **Step 2: `install/README.md`**

- Comparison table: the row `Administrator settings: product name, calendar service account for invitations sent from phones, connecting Outlook mailboxes | — | ✓` becomes `| ✓ | ✓ |`. The row `Administration: mailboxes, domains, aliases, quotas` stays `— | ✓`.
- Step 3.3 table: add the row ``| `Generic__Administrators` | Your own mail address, the one you sign in with. Several? Separate them with commas. |``
- § "Features of the weesky platform": rename it to "Features configured by an administrator". The intro says they are configured from Settings › Administration, by an address listed in `Generic__Administrators`. Keep the two links. Keep "also needs your mail server to be Dovecot" on the delivery replies line. Update every anchor that pointed to the old heading (`grep -n "features-of-the-weesky-platform" -r install README.md docs`).
- After step 3.4 or in § "When the service won't start", add the failure row: ``| `'Generic:Administrators' holds '…', which is not an email address` | Fix that entry in `Generic__Administrators` |``.

- [ ] **Step 3: `README.md`**

In § `generic`, after "Mailboxes, aliases and passwords stay managed wherever they are managed today.", add: "An administrator, named in its settings, still has the screens that need one: the product name and logo, the external mail providers users connect, and the calendar service account."

Check § `weesky`'s "The features that need an administrator" bullet does not now claim they are weesky-only. Reword it if it does.

- [ ] **Step 4: Commit**

```bash
git add install README.md
git commit -F - <<'EOF'
Document the administrators of a generic deployment

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```
