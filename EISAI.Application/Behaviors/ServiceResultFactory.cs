using System.Reflection;
using Eisai.Application.Common;
using FluentValidation.Results;

namespace Eisai.Application.Behaviors;

internal static class ServiceResultFactory
{
    public static bool TryCreateValidationFailure<TResponse>(
        IReadOnlyList<ValidationFailure> failures,
        out TResponse response)
    {
        var errors = failures
            .GroupBy(failure => ToCamelCase(failure.PropertyName))
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray());

        var responseType = typeof(TResponse);
        if (responseType == typeof(ServiceResult))
        {
            response = (TResponse)(object)ServiceResult.Validation(errors);
            return true;
        }

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(ServiceResult<>))
        {
            var method = responseType.GetMethod(
                nameof(ServiceResult<object>.Validation),
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

            response = (TResponse)method!.Invoke(null, [errors])!;
            return true;
        }

        response = default!;
        return false;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
