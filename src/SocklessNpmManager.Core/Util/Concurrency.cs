using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SocklessNpmManager.Core.Util
{
    public static class Concurrency
    {
        /// <summary>Run <paramref name="fn"/> over <paramref name="items"/> with at most <paramref name="limit"/> tasks in flight.</summary>
        public static async Task MapAsync<T>(IReadOnlyList<T> items, int limit, Func<T, int, Task> fn)
        {
            if (items.Count == 0) return;
            var next = 0;
            var gate = new object();
            var degree = Math.Max(1, Math.Min(limit, items.Count));
            var workers = new Task[degree];
            for (var w = 0; w < degree; w++)
            {
                workers[w] = Task.Run(async () =>
                {
                    while (true)
                    {
                        int i;
                        lock (gate)
                        {
                            if (next >= items.Count) return;
                            i = next++;
                        }

                        await fn(items[i], i).ConfigureAwait(false);
                    }
                });
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
        }
    }
}
