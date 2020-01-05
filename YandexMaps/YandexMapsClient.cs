using System;
using System.Linq;
using System.Threading.Tasks;
using LanguageExt;
using PuppeteerSharp;
using static LanguageExt.Prelude;

namespace OnlinerByFlatBot.YandexMaps
{
    public sealed class YandexMapsClient : IDisposable
    {
        static readonly ViewPortOptions ViewPort = new ViewPortOptions
        {
            Width = 1920,
            Height = 1080,
            DeviceScaleFactor = 3
        };

        static readonly ScreenshotOptions ScreenshotOptions = new ScreenshotOptions
        {
            Type = ScreenshotType.Jpeg,
            Quality = 90
        };

        static readonly Func<Location, Location, string> UrlTemplate = (x, y) =>
            $"https://yandex.by/maps/157/minsk/?mode=routes&rtext={x}~{y}&rtt=mt&z=13";

        readonly Page _page;
        YandexMapsClient(Page page) => _page = page;

        public void Dispose()
        {
            _page.Dispose();
            _page.Browser.Dispose();
        }

        public static async Task<YandexMapsClient> Launch()
        {
            var browserFetcher = new BrowserFetcher();
            const int browserRevision = BrowserFetcher.DefaultRevision;
            var revisionInfo = await browserFetcher.DownloadAsync(browserRevision);

            var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                ExecutablePath = revisionInfo.ExecutablePath, // "/usr/local/bin/chromium",
                Headless = true,
                DefaultViewport = ViewPort,
                Args = new[] { "--start-fullscreen" }
            });
            var pages = await browser.PagesAsync();
            var page = pages.Single();

            return new YandexMapsClient(page);
        }

        static async Task<Y> Using<X, Y>(X elm, Func<X, Task<Y>> f) where X : JSHandle
        {
            try
            {
                return await f(elm);
            }
            finally
            {
                await elm.DisposeAsync();
            }
        }

        static Task<Unit> Using<X>(X elm, Func<X, Task> f) where X : JSHandle =>
            Using(elm, x => f(x).ToUnit());

        Task HideElements(params string[] selectors) => _page.EvaluateExpressionAsync(
            $"[{selectors.Map(x => '"' + x + '"').Apply(xs => string.Join(',', xs))}]" +
            ".map(x => [...document.querySelectorAll(x)]).reduce((x, y) => [...x, ...y]).filter(x => x != null).forEach(x => x.setAttribute('style', 'display:none'))"
        );

        async Task<Option<byte[]>> GetRoutesScreenshot()
        {
            await _page.EvaluateExpressionAsync(
                "[...document.querySelectorAll('.route-form-view')].forEach(x => x.classList.remove('_active'))"
            );
            await HideElements(
                ".popup",
                ".lg-cc",
                "[class*=show-details]",
                ".route-list-view__incut",
                ".masstransit-route-form-view__route-hint",
                ".route-list-view__traffic-mode",
                ".route-list-view__mobile-share",
                ".sidebar-panel-header-view",
                ".sidebar-panel-view__close"
            );
            await Task.Delay(3000); // rendering

            var routesListElement = await _page.QuerySelectorAsync(".route-panel-form-view__route-list");
            var routesScreenshot = routesListElement != null
                ? await Using(
                    routesListElement,
                    res => res.ScreenshotDataAsync(ScreenshotOptions)
                ).Map(Some)
                : None;
            return routesScreenshot;
        }

        async Task<byte[]> GetMapScreenshot()
        {
            await Using(await _page.QuerySelectorAsync(".sidebar-toggle-button"), x => x.ClickAsync());
            await HideElements(
                ".header",
                "[class*=copyrights-pane]",
                "[class*=branding-control]",
                ".main-controls",
                ".map-controls-view",
                "._noprint",
                ".map-copyrights",
                ".sidebar-container"
            );
            await Task.Delay(3000); // rendering

            var mapScreenshot = await Using(
                await _page.QuerySelectorAsync(".map-container"),
                elm => elm.ScreenshotDataAsync(ScreenshotOptions)
            );
            return mapScreenshot;
        }

        public async Task<YandexMapsSearchResult> Search(Some<Location> from, Some<Location> to)
        {
            var url = UrlTemplate(from, to);
            await _page.GoToAsync(url, WaitUntilNavigation.Load);

            var routesScreenshot = await GetRoutesScreenshot();
            var mapScreenshot = await GetMapScreenshot();

            return new YandexMapsSearchResult(
                routesScreenshot: routesScreenshot,
                mapScreenshot: mapScreenshot
            );
        }
    }
}
