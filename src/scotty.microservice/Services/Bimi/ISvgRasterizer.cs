namespace weesky.Scotty.Microservice.Services.Bimi;

internal interface ISvgRasterizer
{
    /// <summary>
    /// A square PNG of the given edge in pixels, or null when the drawing cannot be rendered.
    /// Throws <see cref="TimeoutException"/> when no render slot freed up or the render could not finish
    /// in its budget: that is transient, not a verdict on the drawing. The wait never blocks a thread, and
    /// the caller's own cancellation surfaces as an <see cref="OperationCanceledException"/>, never a timeout.
    /// </summary>
    Task<byte[]?> ToPngAsync(byte[] svg, int size, CancellationToken cancellationToken);
}
