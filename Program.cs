using _10xnotes.Components;
using _10xnotes.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
        .UseSnakeCaseNamingConvention());

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
