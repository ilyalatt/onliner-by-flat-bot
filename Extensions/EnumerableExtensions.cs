using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LanguageExt;

namespace OnlinerByFlatBot.Extensions
{
    public static class EnumerableExtensions
    {
        // https://stackoverflow.com/a/13731823
        public static IEnumerable<IEnumerable<TSource>> Batch<TSource>(
            this IEnumerable<TSource> source,
            int size
        ) {
            TSource[] bucket = null;
            var count = 0;

            foreach (var item in source)
            {
                if (bucket == null)
                    bucket = new TSource[size];

                bucket[count++] = item;
                if (count != size)
                    continue;

                yield return bucket;

                bucket = null;
                count = 0;
            }

            if (bucket != null && count > 0)
                yield return bucket.Take(count);
        }

        public static Task<Unit> ToTaskChain<T>(
            this IEnumerable<T> source,
            Func<T, Task> f
        ) => source.Fold(Task.CompletedTask.ToUnit(), (a, x) => a.Bind(_ => f(x).ToUnit()));

        public static Task<T[]> Collect<T>(this IEnumerable<Task<T>> taskSeq) =>
            Task.WhenAll(taskSeq);
    }
}