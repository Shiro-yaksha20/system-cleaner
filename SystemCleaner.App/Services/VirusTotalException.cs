using System;
using System.Net;

namespace SystemCleaner.App.Services;

public sealed class VirusTotalException : Exception
{
    public VirusTotalException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
