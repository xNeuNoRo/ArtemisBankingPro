using ArtemisBankingPro.Api.Middleware;
using ArtemisBankingPro.Api.Infrastructure;
using ArtemisBankingPro.Api.Extensions;
using ArtemisBankingPro.Application;
using ArtemisBankingPro.Infrastructure.Identity;
using ArtemisBankingPro.Infrastructure.Persistence;
using ArtemisBankingPro.Infrastructure.Shared;
using ArtemisBankingPro.Infrastructure.Shared.Observability;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
const long maxRequestBodyBytes = 1_048_576;
long configuredMaxRequestBodyBytes = builder.Configuration.GetValue(
    "Api:MaxRequestBodyBytes",
    maxRequestBodyBytes
);
if (configuredMaxRequestBodyBytes <= 0) {
    throw new InvalidOperationException("Api:MaxRequestBodyBytes debe ser positivo.");
}

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = configuredMaxRequestBodyBytes
);

Log.Logger = SerilogConfiguration
    .CreateArtemisLoggerConfiguration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddIdentityForWebApi(builder.Configuration);
builder.Services.AddSharedInfrastructure(builder.Configuration);
builder.Services
    .AddControllers(options => {
        options.Filters.Add(new AuthorizeFilter());
        options.ConfigureModelBindingMessages();
    })
    .ConfigureApiBehaviorOptions(options => options.ConfigureInvalidModelStateResponse());
builder.Services.AddAuthorization(options => options.AddApiAuthorizationPolicies());
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>()
        .AddOperationTransformer<BearerSecurityOperationTransformer>()
        .AddDocumentTransformer<IdempotencyHeaderTransformer>()
);

builder.Services.PostConfigure<JwtBearerOptions>(
    JwtBearerDefaults.AuthenticationScheme,
    options => {
        options.Events ??= new JwtBearerEvents();
        options.Events.OnChallenge = JwtProblemDetailsEvents.HandleChallengeAsync;
        options.Events.OnForbidden = JwtProblemDetailsEvents.HandleForbiddenAsync;
    }
);

// Global Exception Handler + Problem Details (RFC 7807).
builder.Services.AddProblemDetails(options => {
    options.CustomizeProblemDetails = context => {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging(options => {
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) => {
        string? userId = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        string? role = httpContext.User.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;

        if (!string.IsNullOrWhiteSpace(userId)) {
            diagnosticContext.Set("ActorId", userId);
        }

        if (!string.IsNullOrWhiteSpace(role)) {
            diagnosticContext.Set("ActorRole", role);
        }
    };
});

if (!app.Environment.IsEnvironment("Testing")
    && app.Configuration.GetValue("Api:UseHttpsRedirection", true)) {
    app.UseHttpsRedirection();
}

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) {
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseStatusCodePages(async statusCodeContext => {
    HttpResponse response = statusCodeContext.HttpContext.Response;
    if (
        response.StatusCode is StatusCodes.Status404NotFound
            or StatusCodes.Status405MethodNotAllowed
            or StatusCodes.Status413PayloadTooLarge
            or StatusCodes.Status415UnsupportedMediaType
        && !response.HasStarted
        && string.IsNullOrWhiteSpace(response.ContentType)
    ) {
        var problem = ApiProblemDetailsFactory.FromStatusCode(
            statusCodeContext.HttpContext,
            response.StatusCode
        );
        await ApiProblemDetailsFactory.WriteAsync(statusCodeContext.HttpContext, problem);
    }
});

// Kestrel enforces the limit at the server boundary. This guard also covers
// hosts such as TestServer that do not apply Kestrel limits.
app.Use(async (context, next) => {
    if (context.Request.ContentLength > configuredMaxRequestBodyBytes) {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    await next();
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

bool exposeDocumentation = app.Environment.IsDevelopment()
    || app.Environment.IsEnvironment("Testing")
    || app.Configuration.GetValue("Api:ExposeDocumentation", false);
if (exposeDocumentation) {
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference("/docs").AllowAnonymous();
}

app.MapControllers();

await app.RunAsync();

public partial class Program {
    protected Program() { }
}
