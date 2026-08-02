using _10xnotes.Auth;
using _10xnotes.Components;
using _10xnotes.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Contexts are created per operation by UserScopedDbContextFactory, not held per circuit —
// a Blazor Server scope lives as long as the circuit, which would freeze the user's identity.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
        .UseSnakeCaseNamingConvention(),
    lifetime: ServiceLifetime.Scoped);

// A directly-resolved AppDbContext has no CurrentUserId, so it sees no owned rows. It exists
// only for infrastructure that must resolve the context itself: DataProtection's key store and
// the EF health check.
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

builder.Services.AddScoped<ICurrentUserAccessor, AuthenticationStateCurrentUserAccessor>();
builder.Services.AddScoped<UserScopedDbContextFactory>();

// Without a persisted key ring, every deploy invalidates auth cookies and antiforgery tokens.
// Postgres is used rather than a mounted volume so a rebuilt host cannot silently lose the keys.
builder.Services.AddDataProtection()
    .SetApplicationName("10xnotes")
    .PersistKeysToDbContext<AppDbContext>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Cookie.Name = "10xnotes.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Production is https at the Coolify edge, so the cookie must never travel in clear.
        // Locally, follow the request scheme: with Always, the http launch profile silently
        // drops the cookie and login "succeeds" while leaving the user signed out.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });

// Deny by default: every endpoint without its own authorization metadata requires an
// authenticated user, so a page added by a future slice is protected even if nobody remembers
// to mark it. Everything that must stay public is opted out explicitly with AllowAnonymous —
// the health endpoints and static assets below, [AllowAnonymous] on the auth pages.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddCascadingAuthenticationState();

builder.Services.Configure<SupabaseAuthOptions>(
    builder.Configuration.GetSection(SupabaseAuthOptions.SectionName));

builder.Services.AddHttpClient<SupabaseAuthClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<SupabaseAuthOptions>>().Value;
    client.BaseAddress = new Uri($"{options.Url.TrimEnd('/')}/auth/v1/");
    client.DefaultRequestHeaders.Add("apikey", options.AnonKey);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Migrations self-apply at boot, but a failure must degrade readiness rather than crash the
// process — a crash-loop would fail the container HEALTHCHECK and get the app de-routed.
builder.Services.AddSingleton<DatabaseMigrationState>();
builder.Services.AddHostedService<DatabaseMigrationHostedService>();

// Liveness stays check-free; everything database-backed is tagged "ready" so it can
// only ever affect /health/ready. See the health-endpoint split below.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"])
    .AddCheck<DatabaseMigrationHealthCheck>("migrations", tags: ["ready"]);

var app = builder.Build();

// First in the pipeline, because everything below reads the scheme it restores. Coolify
// terminates TLS at the edge and forwards plain HTTP, so without this Request.Scheme is
// "http" for an https request, and the cookie middleware's challenge answers
// `Location: http://…/login` — verified against the deployed app. HSTS and
// UseHttpsRedirection read the same scheme, so this must precede them too.
//
// The known-proxy lists must be Clear()ed, not initialized to `{ }`: an object initializer
// on a collection property ADDS to it, so `{ }` would leave the default loopback-only entry
// in place. The proxy reaches the container from the Docker network, not 127.0.0.1, so the
// headers would be ignored in exactly the environment this exists for — and the mistake
// hides locally, where requests do come from loopback.
//
// Emptying both lists accepts the headers from any peer, which is safe here: the container
// publishes no port of its own and is reachable only through the Coolify proxy, so there is
// no untrusted client that could forge them.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedHeaders);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Anonymous, or the fallback policy makes the login page render unstyled: its CSS would be
// answered with a redirect to the very page asking for it.
app.MapStaticAssets().AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Liveness: "the process is up". Predicate = _ => false runs NO checks, so the response
// body stays the literal "Healthy" that Dockerfile's HEALTHCHECK and deploy.yml both assert
// on. A database outage must never fail this probe — Coolify de-routes unhealthy containers.
//
// AllowAnonymous is load-bearing on both probes: under the fallback policy they would answer
// 302 → /login, the HEALTHCHECK would stop seeing "Healthy", and Coolify would de-route the
// container — a failure that looks nothing like an auth bug.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();

// Readiness: "the process can actually serve" — includes the database.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.Run();
