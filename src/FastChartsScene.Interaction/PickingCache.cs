using System.Runtime.CompilerServices;
using FastChartsScene.Data;

namespace FastChartsScene.Interaction;

public sealed partial class ChartInteraction
{
    // Source coordinates are immutable, but a custom getter may invoke a UI-thread
    // callback during cold construction. A nested query can finish and cache the same
    // source before this constructor returns. Keep that canonical entry instead of
    // adding a duplicate key. This does not make the controller thread-safe.
    private static T RetainSourceIndex<T>(ConditionalWeakTable<IDataSource, T> indexes,
        IDataSource source, T candidate) where T : class
    {
        if (indexes.TryGetValue(source, out var nested)) return nested;
        indexes.Add(source, candidate); return candidate;
    }
}
