using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using FluentValidation;
using FluentValidation.Results;

namespace ArtemisBankingPro.UnitTests.Application.Common.Errors;

public sealed class ErrorResponseMapperTests {
    private readonly ErrorResponseMapper _mapper = new();

    [Theory]
    [InlineData(ErrorCategory.Validation, 400)]
    [InlineData(ErrorCategory.Conflict, 409)]
    [InlineData(ErrorCategory.Declined, 400)]
    [InlineData(ErrorCategory.NotFound, 404)]
    [InlineData(ErrorCategory.Unauthorized, 401)]
    [InlineData(ErrorCategory.Forbidden, 403)]
    [InlineData(ErrorCategory.PreconditionFailed, 409)]
    public void MapDomainError_MapsCategoryToHttpStatus(
        ErrorCategory category,
        int expectedStatus
    ) {
        var error = new DomainError("Code.Test", "Mensaje de prueba", category);

        ErrorResponse response = _mapper.Map(error);

        response.StatusCode.Should().Be(expectedStatus);
        response.ErrorCode.Should().Be("Code.Test");
        response.Detail.Should().Be("Mensaje de prueba");
    }

    [Fact]
    public void MapDomainError_Validation_HasSolicitudInvalidaTitle() {
        var response = _mapper.Map(
            DomainError.Validation("Code.Validation", "Dato inválido")
        );

        response.Title.Should().Be("Solicitud inválida");
    }

    [Fact]
    public void MapDomainError_NotFound_HasNoEncontradoTitle() {
        var response = _mapper.Map(DomainError.NotFound("Code.NotFound", "No existe"));

        response.Title.Should().Be("No encontrado");
    }

    [Fact]
    public void MapDomainError_Forbidden_HasAccesoDenegadoTitle() {
        var response = _mapper.Map(
            DomainError.Forbidden("Code.Forbidden", "Sin permisos")
        );

        response.Title.Should().Be("Acceso denegado");
    }

    [Fact]
    public void MapDomainError_PreservesStructuredExtensions() {
        var response = _mapper.Map(
            new DomainError(
                "Loan.HighRisk",
                "Cliente de alto riesgo.",
                ErrorCategory.Conflict,
                new Dictionary<string, object?> {
                    ["riskType"] = "ProjectedHighRisk",
                    ["currentDebt"] = 25_000m,
                }
            )
        );

        response.Extensions.Should().ContainKey("riskType");
        ((decimal)response.Extensions!["currentDebt"]!).Should().Be(25_000m);
    }

    [Fact]
    public void MapUnauthenticatedException_Returns401() {
        var response = _mapper.Map(
            new UnauthenticatedException("Debe iniciar sesión.")
        );

        response.StatusCode.Should().Be(401);
        response.Title.Should().Be("No autorizado");
        response.Detail.Should().Be("Debe iniciar sesión.");
    }

    [Fact]
    public void MapForbiddenAccessException_Returns403() {
        var response = _mapper.Map(
            new ForbiddenAccessException("No posee permisos.")
        );

        response.StatusCode.Should().Be(403);
        response.Title.Should().Be("Acceso denegado");
        response.Detail.Should().Be("No posee permisos.");
    }

    [Fact]
    public void MapIdempotencyConflictException_Returns409WithResultReference() {
        var response = _mapper.Map(
            new IdempotencyConflictException("Ya procesada.", "OP-12345")
        );

        response.StatusCode.Should().Be(409);
        response.Title.Should().Be("Conflicto");
        response.Detail.Should().Be("Ya procesada.");
        response.Extensions.Should().ContainKey("resultReference");
        ((string)response.Extensions["resultReference"]!).Should().Be("OP-12345");
    }

    [Fact]
    public void MapFluentValidationException_Returns400WithErrors() {
        var validationException = new ValidationException(
            [
                new ValidationFailure("Monto", "El monto debe ser mayor que cero."),
                new ValidationFailure("Cuenta", "La cuenta es requerida."),
            ]
        );

        ErrorResponse response = _mapper.Map(validationException);

        response.StatusCode.Should().Be(400);
        response.Title.Should().Be("Solicitud inválida");
        response.Extensions.Should().ContainKey("errors");
    }

    [Fact]
    public void MapGenericException_Returns500WithoutLeakingInternals() {
        var response = _mapper.Map(new InvalidOperationException("detalle interno secreto"));

        response.StatusCode.Should().Be(500);
        response.Title.Should().Be("Error interno del servidor");
        response.Detail.Should().NotContain("detalle interno secreto");
    }
}
