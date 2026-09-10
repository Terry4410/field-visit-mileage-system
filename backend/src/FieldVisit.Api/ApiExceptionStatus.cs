using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Api;

public static class ApiExceptionStatus
{
    public static int From(Exception? ex) => ex switch
    {
        UnauthorizedAccessException => 403,
        KeyNotFoundException => 404,
        DbUpdateConcurrencyException => 409,
        InvalidOperationException when ex.Message.Contains("ROWVERSION_CONFLICT", StringComparison.OrdinalIgnoreCase) => 409,
        InvalidOperationException => 422,
        _ => 500
    };
}
