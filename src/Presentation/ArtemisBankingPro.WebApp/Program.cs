using ArtemisBankingPro.Application;
using ArtemisBankingPro.Infrastructure.Identity;
using ArtemisBankingPro.Infrastructure.Persistence;
using ArtemisBankingPro.Infrastructure.Shared;
using ArtemisBankingPro.Infrastructure.Shared.Observability;
using ArtemisBankingPro.WebApp.Middleware;
using Microsoft.AspNetCore.Mvc;
using System.Threading.RateLimiting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

Log.Logger = SerilogConfiguration
    .CreateArtemisLoggerConfiguration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())
);
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) => {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        Microsoft.Extensions.Logging.ILogger logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("ArtemisBankingPro.WebApp.RateLimiting");
        logger.LogWarning(
            "WebApp authentication rate limit exceeded for {Path}; trace {TraceIdentifier}",
            context.HttpContext.Request.Path,
            context.HttpContext.TraceIdentifier
        );
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}:{context.Request.Path}",
            _ => new FixedWindowRateLimiterOptions {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }
        )
    );
});
builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddIdentityForWebApp(builder.Configuration, builder.Environment);
builder.Services.AddSharedInfrastructure(builder.Configuration);

var app = builder.Build();

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();

app.Use(async (context, next) => {
    await next();

    // Re-execute only safe 404 navigations. A POST rejected by antiforgery or
    // another method/status must keep its original response contract.
    if (context.Response.StatusCode != StatusCodes.Status404NotFound
        || !HttpMethods.IsGet(context.Request.Method)
        || context.Request.Path == "/Home/NotFound") {
        return;
    }

    context.Response.Clear();
    context.Request.Path = "/Home/NotFound";
    context.Request.QueryString = QueryString.Empty;
    context.Request.RouteValues.Clear();
    context.SetEndpoint(null);
    await next();
});
if (app.Configuration.GetValue("WebApp:UseHttpsRedirection", true)) {
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

await app.RunAsync();

public partial class Program {
    protected Program() { }
}
