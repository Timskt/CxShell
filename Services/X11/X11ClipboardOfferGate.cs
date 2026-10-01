namespace CxShell.Services.X11;

internal sealed class X11ClipboardOfferGate<TSource> where TSource : class
{
    private long _revision;

    public void Invalidate() => Interlocked.Increment(ref _revision);

    public async Task OfferAsync(
        TSource source,
        Func<Task<string?>> readClipboard,
        Func<TSource, bool> isSourceActive,
        Action<string> applyClipboard)
    {
        long revision = Interlocked.Increment(ref _revision);
        string? text = await readClipboard();

        if (revision != Volatile.Read(ref _revision) || !isSourceActive(source))
        {
            return;
        }

        applyClipboard(text ?? string.Empty);
    }
}
