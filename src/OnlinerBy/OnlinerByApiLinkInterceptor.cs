using System;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace OnlinerByFlatBot.OnlinerBy {
    public static class OnlinerByApiLinkInterceptor {
        static async Task<OnlinerApiLink> InterceptImpl(string onlinerUrl) {
            var timeout = TimeSpan.FromSeconds(30);
            var timeoutCts = new TaskCompletionSource(timeout);
            var timeoutTask = timeoutCts.Task;
            
            var resultTcs = new TaskCompletionSource<string>();
            var resultTask = resultTcs.Task;
            await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions {
                Headless = true
            });
            var page = await browser.NewPageAsync();
            page.Request += async (_, args) => {
                var request = args.Request;
                var url = request.Url;
                if (url.StartsWith("https://r.onliner.by/sdapi/ak.api/search/apartments")) {
                    resultTcs.SetResult(url);
                }
                await request.ContinueAsync();
            };
            await page.SetRequestInterceptionAsync(true);
            
            var navigationTask = page.GoToAsync(onlinerUrl, WaitUntilNavigation.DOMContentLoaded);
            await Task.WhenAny(navigationTask, resultTask, timeoutTask);
            if (!resultTask.IsCompleted) {
                await Task.WhenAny(navigationTask, timeoutTask);
            }
            await Task.WhenAny(resultTask, timeoutTask);
            if (timeoutTask.IsFaulted) {
                await timeoutTask;
            }
            var apiUrl = await resultTask;
            return new OnlinerApiLink(apiUrl, "");
        }

        public static async Task<OnlinerApiLink> Intercept(string onlinerUrl) {
            try {
                return await InterceptImpl(onlinerUrl);
            }
            catch (OperationCanceledException) {
                throw new TimeoutException("Onliner API link interception failed by timeout.");
            }
        }
    }
}