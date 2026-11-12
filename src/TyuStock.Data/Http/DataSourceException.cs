namespace TyuStock.Data.Http;

public enum DataErrorKind { Network, Timeout, Http, RateLimited, Blocked, Parse, NoData, Unsupported }

/// <summary>Any failure talking to (or interpreting) a data source. The failover router treats all of these as "try the next source".</summary>
public sealed class DataSourceException : Exception
{
    public string SourceId { get; }
    public DataErrorKind Kind { get; }
    public int? StatusCode { get; }

    public DataSourceException(string sourceId, DataErrorKind kind, string message, int? statusCode = null, Exception? inner = null)
        : base($"[{sourceId}] {kind}: {message}", inner)
    {
        SourceId = sourceId; Kind = kind; StatusCode = statusCode;
    }

    /// <summary>Whether retrying the same request might succeed.</summary>
    public bool IsTransient => Kind is DataErrorKind.Network or DataErrorKind.Timeout or DataErrorKind.RateLimited
        || (Kind == DataErrorKind.Http && StatusCode is >= 500 or 408);
}
