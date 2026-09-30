namespace Mrp.SharedKernel.Domain;

/// <summary>
/// A business rule was violated. <see cref="Code"/> is a stable key that clients translate (i18n);
/// <see cref="Exception.Message"/> is an English fallback for logs.
/// </summary>
public class DomainException : Exception
{
    /// <summary>Creates a rule violation.</summary>
    /// <param name="code">Stable error code such as <c>inventory.insufficient_stock</c>.</param>
    /// <param name="message">English description.</param>
    /// <param name="statusCode">HTTP status to map to (default 422).</param>
    public DomainException(string code, string message, int statusCode = 422)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>Stable error code.</summary>
    public string Code { get; }

    /// <summary>HTTP status the API layer should return.</summary>
    public int StatusCode { get; }

    /// <summary>Requested resource does not exist in the current tenant.</summary>
    public static DomainException NotFound(string what, object key) =>
        new("common.not_found", $"{what} '{key}' was not found.", 404);

    /// <summary>The operation conflicts with the current state of the resource.</summary>
    public static DomainException Conflict(string code, string message) => new(code, message, 409);

    /// <summary>The caller is not allowed to perform the operation.</summary>
    public static DomainException Forbidden(string code, string message) => new(code, message, 403);
}
