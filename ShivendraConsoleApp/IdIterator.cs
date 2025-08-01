using System.Threading;

namespace ShivendraConsoleApp;

internal static class IdIterator
{
    private static object _lock = new();
    private static int _idx;
    private static string[] _ids = null!;

    internal static void Configure(string[] _ids)
    {
        IdIterator._ids = _ids;
        _idx = 0;
    }

    internal static int? GetCurrentIdx()
    {
        lock (_lock)
        {
            return _idx == _ids.Length ? null : _idx;
        }
    }

    internal static void Complete(CancellationToken token)
    {
        lock (_lock)
        {
            if (!token.IsCancellationRequested) ++_idx;
        }
    }
}