using System;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace OnlinerByFlatBot.Extensions
{
    public static class RxHelpers
    {
        // fuck i want my async seq from f# instead of this hot shit
        class CtsDisposable : IDisposable
        {
            public CancellationTokenSource Cts { get; }

            public CtsDisposable(CancellationTokenSource cts) =>
                Cts = cts ?? throw new ArgumentNullException(nameof(cts));

            public void Dispose()
            {
                Cts.Cancel();
                Cts.Dispose();
            }
        }

        public static IObservable<T> Generate<T>(Func<Task<T>> provider) =>
            new AnonymousObservable<T>(obs =>
            {
                var cts = new CancellationTokenSource();
                var ct = cts.Token;

                async Task RunGenerator()
                {
                    while (!ct.IsCancellationRequested) obs.OnNext(await provider());
                    obs.OnCompleted();
                }

                RunGenerator().ContinueWith(x => obs.OnError(x.Exception), TaskContinuationOptions.OnlyOnFaulted);

                return new CtsDisposable(cts);
            });
    }
}