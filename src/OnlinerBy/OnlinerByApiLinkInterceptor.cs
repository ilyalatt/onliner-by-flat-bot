using System;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace OnlinerByFlatBot.OnlinerBy {
    public static class OnlinerByApiLinkInterceptor {
        static async Task<OnlinerApiLink> Intercept(string onlinerUrl, CancellationToken ct) {
            var tcs = new TaskCompletionSource<string>();
            await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true
            });
            await Task.Delay(1000); // avoids PuppeteerSharp.NavigationException: Navigation failed because browser has disconnected!
            var page = await browser.NewPageAsync();
            page.Request += async (_, args) => {
                var request = args.Request;
                var url = request.Url;
                if (url.Contains("api.onliner.by") && url.Contains("apartments")) tcs.SetResult(url);
                await request.ContinueAsync();
            };
            await page.SetRequestInterceptionAsync(true);
            await page.GoToAsync(onlinerUrl);
            var apiUrlTask = tcs.Task;
            var apiUrl = await tcs.Task;
            return new OnlinerApiLink(apiUrl, "");
        }

        public static async Task<OnlinerApiLink> Intercept(string onlinerUrl) {
            var timeout = TimeSpan.FromSeconds(30);
            var cts = new CancellationTokenSource(timeout);
            try {
                return await Intercept(onlinerUrl, cts.Token);
            }
            catch (OperationCanceledException) {
                throw new TimeoutException($"Onliner API link interception failed by timeout ({timeout}).");
            }
        }
    }
}