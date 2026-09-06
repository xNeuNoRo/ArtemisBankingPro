using ArtemisBankingPro.Api.Middleware;
using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Application.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

/// <summary>
/// Verifica el Global Exception Handler con el mapper real de Application:
/// genera Problem Details (RFC 7807) sin exponer detalles internos.
/// </summary>
public sealed class GlobalExceptionHandlerTests {
    private static GlobalExceptionHandler CreateHandler() {
        var logger = NullLogger<GlobalExceptionHandler>.Instance;
        var mapper = new ErrorResponseMapper();
        return new GlobalExceptionHandler(mapper, logger);
    }

    [Fact]
    public async Task TryHandleAsync_ForbiddenException_Returns403ProblemDetails() {
        var handler = CreateHandler();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        bool handled = await handler.TryHandleAsync(
            context,
            new ForbiddenAccessException("No posee permisos."),
            CancellationToken.None
        );

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(403);
        context.Response.ContentType.Should().Contain("application/problem+json");

        context.Response.Body.Position = 0;
        var problem = await System.Text.Json.JsonSerializer.DeserializeAsync<ProblemDetails>(
            context.Response.Body
        );
        Assert.NotNull(problem);
        problem.Title.Should().Be("Acceso denegado");
        problem.Detail.Should().Be("No posee permisos.");
        problem.Extensions.Should().ContainKey("errorCode");
    }

    [Fact]
    public async Task TryHandleAsync_UnauthenticatedException_Returns401() {
        var handler = CreateHandler();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        bool handled = await handler.TryHandleAsync(
            context,
            new UnauthenticatedException("Debe iniciar sesión."),
            CancellationToken.None
        );

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task TryHandleAsync_IdempotencyConflict_Returns409WithResultReference() {
        var handler = CreateHandler();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        bool handled = await handler.TryHandleAsync(
            context,
            new IdempotencyConflictException("Ya procesada.", "OP-777"),
            CancellationToken.None
        );

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(409);

        context.Response.Body.Position = 0;
        var problem = await System.Text.Json.JsonSerializer.DeserializeAsync<ProblemDetails>(
            context.Response.Body
        );
        Assert.NotNull(problem);
        problem.Extensions.Should().ContainKey("resultReference");
        ((System.Text.Json.JsonElement)problem.Extensions["category"]!)
            .GetString()
            .Should()
            .Be("Conflict");
        ((System.Text.Json.JsonElement)problem.Extensions["resultReference"]!)
            .GetString()
            .Should()
            .Be("OP-777");
    }

    [Fact]
    public async Task TryHandleAsync_GenericException_Returns500WithoutLeakingDetails() {
        var handler = CreateHandler();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        bool handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("secreto interno de conexion"),
            CancellationToken.None
        );

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(500);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().NotContain("secreto interno de conexion");
    }
}
