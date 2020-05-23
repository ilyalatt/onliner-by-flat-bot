using System;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace OnlinerByFlatBot.OnlinerBy {
    public sealed class OnlinerByApiLinkInterceptor : IAsyncDisposable {
        readonly Browser _browser;
        OnlinerByApiLinkInterceptor(Browser browser) => _browser = browser;

        public async ValueTask DisposeAsync()
        {
            await _browser.DisposeAsync();
        }
        
        public static async Task<OnlinerByApiLinkInterceptor> Launch() {
            var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true
            });
            return new OnlinerByApiLinkInterceptor(browser);
        }
        
        public async Task<OnlinerApiLink> Intercept(string onlinerUrl) {
            var tcs = new TaskCompletionSource<string>();
            var page = await _browser.NewPageAsync();
            page.Request += async (_, args) => {
                var request = args.Request;
                var url = request.Url;
                if (url.Contains("api.onliner.by") && url.Contains("apartments")) tcs.SetResult(url);
                await request.ContinueAsync();
            };
            await page.SetRequestInterceptionAsync(true);
            try {
                await page.GoToAsync(onlinerUrl);
                var apiUrl = await tcs.Task;
                return new OnlinerApiLink(apiUrl, "");
            }
            finally {
                await page.CloseAsync();
            }
        }
    }
}