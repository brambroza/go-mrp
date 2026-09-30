using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Mrp.SharedKernel.Web;

/// <summary>
/// Endpoint filter that validates a request body with DataAnnotations, including nested objects and
/// lists, and returns RFC 7807 validation problems (HTTP 400).
/// </summary>
/// <typeparam name="T">Request body type.</typeparam>
public sealed class ValidationFilter<T> : IEndpointFilter
    where T : class
{
    private const int MaxDepth = 6;

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var body = context.Arguments.OfType<T>().FirstOrDefault();
        if (body is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["Request body is required."] });
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        Validate(body, string.Empty, errors, 0);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }

        return await next(context);
    }

    private static void Validate(object instance, string prefix, Dictionary<string, List<string>> errors, int depth)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        foreach (var result in results)
        {
            var members = result.MemberNames.Any() ? result.MemberNames : [string.Empty];
            foreach (var member in members)
            {
                var key = ToCamelCase(prefix + member);
                if (!errors.TryGetValue(key, out var list))
                {
                    errors[key] = list = [];
                }

                list.Add(result.ErrorMessage ?? "Invalid value.");
            }
        }

        if (depth >= MaxDepth)
        {
            return;
        }

        foreach (var property in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || IsSimple(property.PropertyType))
            {
                continue;
            }

            var value = property.GetValue(instance);
            if (value is null)
            {
                continue;
            }

            if (value is IEnumerable sequence)
            {
                var index = 0;
                foreach (var item in sequence)
                {
                    if (item is not null && !IsSimple(item.GetType()))
                    {
                        Validate(item, $"{prefix}{property.Name}[{index}].", errors, depth + 1);
                    }

                    index++;
                }
            }
            else
            {
                Validate(value, $"{prefix}{property.Name}.", errors, depth + 1);
            }
        }
    }

    private static bool IsSimple(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        return actual.IsPrimitive || actual.IsEnum || actual == typeof(string) || actual == typeof(decimal)
            || actual == typeof(Guid) || actual == typeof(DateTime) || actual == typeof(DateTimeOffset)
            || actual == typeof(DateOnly) || actual == typeof(TimeOnly) || actual == typeof(TimeSpan)
            || actual.Namespace == "System.Text.Json";
    }

    private static string ToCamelCase(string path) =>
        string.Join('.', path.Split('.').Select(p => p.Length > 0 ? char.ToLowerInvariant(p[0]) + p[1..] : p));
}

/// <summary>Endpoint helpers for request validation.</summary>
public static class ValidationEndpointExtensions
{
    /// <summary>Validates the request body of type <typeparamref name="T"/> before the handler runs.</summary>
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder)
        where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
