using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;

namespace OnlinerByFlatBot.Extensions
{
    public static class RxExtensions
    {
        public static IObservable<(TSource, TSource)> Pairwise<TSource>(
            this IObservable<TSource> source
        ) => source.Scan(
            (default(TSource), default(TSource)),
            (previous, current) => (previous.Item2, current)
        ).Skip(1);
        
        public static AsyncSubject<Unit> ToTaskChain<T>(
            this IObservable<T> source,
            Func<T, Task> f
        ) => source.Scan(Task.CompletedTask.ToUnit(), (a, x) => a.Bind(_ => f(x).ToUnit()))
            .SelectMany(identity).RunAsync(CancellationToken.None);
        
        // https://stackoverflow.com/a/44680937
        public static IObservable<T> ToOrdinalOrder<T>(this IObservable<T> source, Func<T, int> indexSelector) => source
            .Scan(
                (expectedIndex: 0, heldItems: new Dictionary<int, T>(), toReturn: Enumerable.Empty<T>()),
                (state, item) =>
                {
                    var itemIndex = indexSelector(item);
                    if (itemIndex == state.expectedIndex)
                    {
                        var heldItems = state.heldItems;
                        var indexes = Enumerable.Range(itemIndex + 1, heldItems.Count).Filter(heldItems.ContainsKey).ToList();
                        var expectedIndex = itemIndex + 1 + indexes.Count;
                        var toReturn = new[] { item }.Concat(indexes.Map(x => heldItems[x])).ToList();
                        indexes.Iter(x => heldItems.Remove(x));
                        return (expectedIndex, heldItems, toReturn);
                    }

                    state.heldItems.Add(itemIndex, item);
                    return (state.expectedIndex, state.heldItems, Enumerable.Empty<T>());
                }
            )
            .SelectMany(t => t.toReturn);

        public static IObservable<TResult> OrderedSelectMany<TSource, TResult>(
            this IObservable<TSource> source,
            Func<TSource, Task<TResult>> selector
        ) => source
            .SelectMany(async (TSource item, int index) => (await selector(item), index))
            .ToOrdinalOrder(t => t.Item2)
            .Select(t => t.Item1);
    }
}