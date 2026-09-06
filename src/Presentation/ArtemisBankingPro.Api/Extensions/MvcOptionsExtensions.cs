using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Extensions;

public static class MvcOptionsExtensions {
    public static void ConfigureModelBindingMessages(this MvcOptions options) {
        var provider = options.ModelBindingMessageProvider;

        provider.SetAttemptedValueIsInvalidAccessor(
            (_, field) => $"El valor proporcionado no es válido para el campo '{field}'."
        );
        provider.SetUnknownValueIsInvalidAccessor(
            field => $"El valor proporcionado no es válido para el campo '{field}'."
        );
        provider.SetNonPropertyAttemptedValueIsInvalidAccessor(
            _ => "El valor proporcionado no es válido."
        );
        provider.SetNonPropertyUnknownValueIsInvalidAccessor(
            () => "El valor proporcionado no es válido."
        );
        provider.SetValueIsInvalidAccessor(_ => "El valor proporcionado es inválido.");
        provider.SetValueMustNotBeNullAccessor(field => $"El valor '{field}' no puede ser nulo.");
        provider.SetMissingRequestBodyRequiredValueAccessor(
            () => "El cuerpo de la petición no puede estar vacío."
        );
        provider.SetMissingBindRequiredValueAccessor(
            field => $"Falta proporcionar un valor para el campo requerido '{field}'."
        );
        provider.SetMissingKeyOrValueAccessor(() => "Se requiere proporcionar un valor.");
        provider.SetNonPropertyValueMustBeANumberAccessor(
            () => "El valor debe ser un número válido."
        );
        provider.SetValueMustBeANumberAccessor(
            field => $"El campo '{field}' debe ser un número válido."
        );
    }
}
