namespace HexCrawl.Application;

public sealed class MapAnalysisUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class MapAnalysisTimeoutException(string message, Exception? innerException = null)
    : TimeoutException(message, innerException);

public sealed class MapAnalysisAuthenticationException(string message)
    : Exception(message);

public sealed class MapAnalysisProtocolException(string message, Exception? innerException = null)
    : Exception(message, innerException);
