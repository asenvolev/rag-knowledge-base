namespace RagKb.Api.Infrastructure;

/// <summary>Client error that is safe to show to the user (mapped to HTTP 400).</summary>
public sealed class BadRequestException(string message) : Exception(message);

/// <summary>Resource not found (mapped to HTTP 404).</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>Upstream AI provider failure (mapped to HTTP 502).</summary>
public sealed class AiProviderException(string message, Exception? inner = null) : Exception(message, inner);
