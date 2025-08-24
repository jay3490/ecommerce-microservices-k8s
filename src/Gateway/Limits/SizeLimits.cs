using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Gateway.Limits;

public sealed class SizeLimitOptions
{
    public const string SectionName = "Gateway:Limits";

    /// <summary>Largest request body the gateway accepts and forwards.</summary>
    public long MaxRequestBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>Largest response body the gateway relays from a service.</summary>
    public long MaxResponseBodyBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>
/// Enforces both size limits. Requests that declare a larger body are refused with 413
/// before anything is forwarded, and the server's body size limit stops chunked bodies that
/// grow past it. Responses are checked in <see cref="AddResponseSizeCheck"/> when they
/// declare their length; a response that does not is cut off once it passes the limit.
/// </summary>
public sealed class SizeLimitMiddleware(RequestDelegate next, IOptions<SizeLimitOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var limits = options.Value;
        if (context.Request.ContentLength > limits.MaxRequestBodyBytes)
        {
            await Results.Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: $"The request body is larger than the {limits.MaxRequestBodyBytes}-byte limit.")
                .ExecuteAsync(context);
            return;
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
        {
            bodySize.MaxRequestBodySize = limits.MaxRequestBodyBytes;
        }

        var original = context.Response.Body;
        context.Response.Body = new ResponseSizeLimitStream(original, limits.MaxResponseBodyBytes);
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = original;
        }
    }

    /// <summary>Turns a proxied response that declares an oversized body into a 502.</summary>
    public static void AddResponseSizeCheck(TransformBuilderContext context) =>
        context.AddResponseTransform(transform =>
        {
            var limit = transform.HttpContext.RequestServices.GetRequiredService<IOptions<SizeLimitOptions>>().Value.MaxResponseBodyBytes;
            if (transform.ProxyResponse?.Content.Headers.ContentLength is { } length && length > limit)
            {
                var response = transform.HttpContext.Response;
                response.Headers.Clear();
                response.StatusCode = StatusCodes.Status502BadGateway;
                response.ContentLength = 0;
                transform.SuppressResponseBody = true;
            }

            return ValueTask.CompletedTask;
        });
}

/// <summary>Passes writes through until the total passes the limit, then throws.</summary>
public sealed class ResponseSizeLimitStream(Stream inner, long limit) : Stream
{
    private long _written;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Count(count);
        inner.Write(buffer, offset, count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Count(buffer.Length);
        await inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private void Count(int bytes)
    {
        _written += bytes;
        if (_written > limit)
        {
            throw new InvalidOperationException($"The response body passed the {limit}-byte limit.");
        }
    }
}
