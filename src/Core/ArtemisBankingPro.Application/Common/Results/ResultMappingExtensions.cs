using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Common.Results;

internal static class ResultMappingExtensions {
    public static Result<TDestination> MapValue<TSource, TDestination>(
        this Result<TSource> result,
        Func<TSource, TDestination> map
    ) => result.IsSuccess
        ? Result.Success(map(result.Value))
        : Result.Failure<TDestination>(result.Error!);

    public static Result ToUnit<TSource>(this Result<TSource> result) =>
        result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);
}
