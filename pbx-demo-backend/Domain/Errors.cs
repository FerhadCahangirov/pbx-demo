namespace CallControl.Api.Domain;

public class AppException : Exception
{
    public int ErrorCode { get; }
    public string ErrorName { get; }

    public AppException(string name, string message, int errorCode) : base(message)
    {
        ErrorName = name;
        ErrorCode = errorCode;
    }
}

public sealed class BadRequestException : AppException
{
    public BadRequestException(string message) : base("Bad Request", message, 400) { }
}

public sealed class UnauthorizedException : AppException
{
    public UnauthorizedException(string message) : base("Unauthorized", message, 401) { }
}

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message) : base("Forbidden", message, 403) { }
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string message) : base("Not Found", message, 404) { }
}

public sealed class InternalServerErrorException : AppException
{
    public InternalServerErrorException(string message) : base("Internal Server Error", message, 500) { }
}

public sealed class UpstreamApiException : AppException
{
    public UpstreamApiException(string message, int statusCode)
        : base("Upstream API Error", message, statusCode)
    {
    }
}
