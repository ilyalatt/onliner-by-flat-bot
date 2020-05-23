using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OnlinerByFlatBot.OnlinerBy.Model;
using Flurl.Http;
using LanguageExt;
using Newtonsoft.Json.Linq;
using static LanguageExt.Prelude;

namespace OnlinerByFlatBot.OnlinerBy
{
    public sealed class OnlinerByClient : IDisposable
    {
        readonly FlurlClient _flurlClient;
        readonly string _url;

        public void Dispose() => _flurlClient.Dispose();

        public OnlinerByClient(Some<OnlinerApiLink> someApiLink)
        {
            var apiLink = someApiLink.Value;
            _url = apiLink.Url;

            var cookies = new CookieContainer();
            apiLink.Cookies.Apply(Optional).Iter(cookieStr => cookieStr
                .Split("; ", StringSplitOptions.RemoveEmptyEntries).Map(x => x.Split('=', 2)).Map(x => (x[0], x[1]))
                .Map(t => new Cookie(t.Item1, t.Item2, "/", "r.onliner.by")).Iter(cookies.Add)
            );

            _flurlClient = new HttpClient(
                new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    CookieContainer = cookies
                }
            ).Apply(x => new FlurlClient(x));
        }

        Task<string> GetLatestUpdateJson() => _url
            .WithClient(_flurlClient)
            .WithHeader("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_14_2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/71.0.3578.98 Safari/537.36")
            .WithHeader("Accept", "application/json, text/plain, */*")
            .WithHeader("Accept-Language", "en-US,en;q=0.9,ru;q=0.8")
            .WithHeader("Origin", "https://r.onliner.by")
            .WithHeader("Referer", "https://r.onliner.by/pk/")
            .GetStringAsync();

        static Task<string> GetTestData() =>
            File.ReadAllTextAsync("resp.txt");

        public async Task<Arr<Flat>> GetLatestUpdate()
        {
            var json = await GetLatestUpdateJson();// GetTestData();
            var root = JToken.Parse(json);
            var flats = root["apartments"].Map(x => new Flat(
                id: x["id"].Value<int>(),
                url: x["url"].Value<string>(),
                photoUrl: x["photo"].Value<string>(),
                type: x["rent_type"].Value<string>(),
                address: x["location"]["user_address"].Value<string>(),
                location: x["location"].Apply(y => new Location(y["latitude"].Value<double>(), y["longitude"].Value<double>())),
                isOwner: x["contact"]["owner"].Value<bool>(),
                price: UsdPrice.Parse(x["price"]["converted"]["USD"]["amount"].Value<string>()),
                createdAt: x["created_at"].Value<DateTime>(),
                updatedAt: x["last_time_up"].Value<DateTime>()
            )).ToArr();
            return flats;
        }

        public async Task<Arr<string>> GetPhotosUrls(Some<string> url)
        {
            var html = await url.Value
                .WithClient(_flurlClient)
                .GetStringAsync();
            var regex = new Regex("<div style=\"background-image: url\\((.+?)\\)\".+?class=\"(.+?)\".*?>", RegexOptions.Singleline);
            return regex.Matches(html).Map(m => m.Groups).Map(g => (g[1].Value, g[2].Value))
                .Filter(t => t.Item2.Contains("apartment-gallery__slide")).Map(t => t.Item1).ToArr();
        }

        public string GetExtension(Some<string> url) =>
            url.Value.Apply(Path.GetExtension);

        readonly SemaphoreSlim _semaphore = new SemaphoreSlim(3, 3);
        public async Task<byte[]> DownloadPhoto(Some<string> url)
        {
            await _semaphore.WaitAsync();
            try
            {
                return await url.Value.WithClient(_flurlClient).GetBytesAsync();
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
