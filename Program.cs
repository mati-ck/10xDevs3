using _10xnotes.Components;
using _10xnotes.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

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
        // Production is https at the Coolify edge; use the https launch profile locally.
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Liveness: "the process is up". Predicate = _ => false runs NO checks, so the response
// body stays the literal "Healthy" that Dockerfile's HEALTHCHECK and deploy.yml both assert
// on. A database outage must never fail this probe — Coolify de-routes unhealthy containers.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });

// Readiness: "the process can actually serve" — includes the database.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();
