using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Common.Errors;

/// <summary>
/// Mapea errores de dominio y excepciones conocidas a una
/// <see cref="ErrorResponse"/> estable, compartida por la API y la WebApp.
/// Es un componente puro de Application: no depende de HTTP.
/// </summary>
public interface IErrorResponseMapper {
    ErrorResponse Map(DomainError domainError);

    ErrorResponse Map(Exception exception);
}
