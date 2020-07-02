using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using OnlinerByFlatBot.Extensions;
using OnlinerByFlatBot.OnlinerBy;
using OnlinerByFlatBot.OnlinerBy.Model;
using OnlinerByFlatBot.YandexMaps;
using Flurl.Http;
using LanguageExt;
using LanguageExt.SomeHelp;
using NodaTime;
using NodaTime.Extensions;
using Polly;
using Polly.Retry;
using PuppeteerSharp;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InputFiles;
using static LanguageExt.Prelude;
using Unit = LanguageExt.Unit;

namespace OnlinerByFlatBot
{
    static class Program
    {
        static Task<T> Delay<T>(Task<T> task, int delayInMs) =>
            task.Bind(r => Task.Delay(delayInMs).ToUnit().Map(_ => r));

        static Task<T> DelayRnd<T>(Task<T> task) =>
            Delay(task, Rnd.NextInt32(2000, 5000));

        static IObservable<Arr<Flat>> GetOnlinerFlatScrapeStream(OnlinerByClient onlinerClient) =>
            RxHelpers.Generate(() => onlinerClient.GetLatestUpdate().Apply(DelayRnd));

        static IObservable<Flat> GetOnlinerFlatUpdatesStream(OnlinerByClient onlinerClient, DateTime lastScrapedEntityDate) =>
            GetOnlinerFlatScrapeStream(onlinerClient)
            .SelectMany(xs => xs.OrderBy(x => x.UpdatedAt))
            .Scan((lastUpdatedAt: lastScrapedEntityDate, flat: Option<Flat>.None),
                (s, x) => x.UpdatedAt <= s.lastUpdatedAt ? (s.lastUpdatedAt, Option<Flat>.None) : (x.UpdatedAt, x)
            )
            .SelectMany(s => s.flat);

        static TimeSpan ExpDelay(int retryNumber) =>
            Math.Pow(2, retryNumber).Apply(x => x * 1000)
            .Apply(x => x + Rnd.NextInt32(0, (int) (x / 3)))
            .Apply(TimeSpan.FromMilliseconds);

        static readonly AsyncRetryPolicy TelegramPollyPolicy = Policy
            .Handle<ApiRequestException>()
            .WaitAndRetryAsync(6, ExpDelay);

        static readonly AsyncRetryPolicy OnlinerByPollyPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<OperationCanceledException>()
            .Or<FlurlHttpException>(x => x.Call.HttpResponseMessage.StatusCode != HttpStatusCode.NotFound)
            .WaitAndRetryAsync(6, ExpDelay);

        static readonly AsyncRetryPolicy YandexMapsPollyPolicy = Policy
            .Handle<PuppeteerException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(6, ExpDelay);

        static readonly AsyncRetryPolicy BotPollyPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryForeverAsync(sleepDurationProvider: _ => TimeSpan.FromSeconds(10), onRetry: (ex, _) => Console.WriteLine(ex));

        static Func<ITelegramBotClient, Task<T>> TelegramWithDelayAndPolly<T>(Func<ITelegramBotClient, Task<T>> f) =>
            async x => {
                await Task.Delay(500);
                return await TelegramPollyPolicy.ExecuteAsync(() => f(x));
            };

        static Func<OnlinerByClient, Task<T>> OnlinerByWithPolly<T>(Func<OnlinerByClient, Task<T>> f) =>
            x => OnlinerByPollyPolicy.ExecuteAsync(() => f(x));

        static Func<YandexMapsClient, Task<T>> YandexMapsWithPolly<T>(Func<YandexMapsClient, Task<T>> f) =>
            x => YandexMapsPollyPolicy.ExecuteAsync(() => f(x));

        static Func<Flat, Task<Unit>> HandleFlat(
            ITelegramBotClient tg,
            OnlinerByClient onlinerClient,
            YandexMapsClient yandexMapsClient,
            Location routeDestination,
            ChatId chatId,
            string channelName,
            State state
        ) => async flat =>
        {
            Task<(byte[], string)> DownloadPhoto(string url) =>
                onlinerClient.Apply(OnlinerByWithPolly(x => x.DownloadPhoto(url)
                .Map(bts => (
                    photo: bts,
                    ext: x.GetExtension(url)
                ))));
            
            const int PhotoSizeLimit = 10 * 1024 * 1024;
            static bool IsAcceptablePhoto(byte[] photo) => photo.Length < PhotoSizeLimit;

            Task SendPhotoResiliently(
                byte[] photo,
                string fileName,
                string caption = null,
                bool disableNotification = true
            ) =>
                TelegramWithDelayAndPolly(x =>
                    x.SendPhotoAsync(
                        chatId: chatId,
                        photo: new InputOnlineFile(
                            new MemoryStream(photo),
                            fileName
                        ),
                        caption: caption,
                        disableNotification: disableNotification
                    )
                )(tg);

            var timeSinceCreation = Period.Between(flat.CreatedAt.ToLocalDateTime(), flat.UpdatedAt.ToLocalDateTime());
            var isNew = timeSinceCreation.Equals(Period.Zero);
            var type = flat.Type.Apply(FlatType.Match(
                _: () => flat.Type,
                room: () => "комната",
                flatWithNRooms: n => n + "-комнатная"
            ));
            var txt = new [] {
                $"{(isNew ? "Новая хата" : $"UP, создано {timeSinceCreation.Humanize()} назад")}",
                $"${flat.Price.Dollars}, {type}",
                $"{flat.Address.TrimStart("Минск, ")}",
                flat.Url
            }.Apply(xs => string.Join(Environment.NewLine, xs));

            try
            {
                var (photo, ext) = await DownloadPhoto(flat.PhotoUrl);
                await SendPhotoResiliently(photo, "main" + ext, txt, disableNotification: false);
            }
            catch (FlurlHttpException e) when (e.Call.HttpResponseMessage.StatusCode == HttpStatusCode.NotFound)
            {
                return unit;
            }

            Task SendPhotosResiliently(IEnumerable<IAlbumInputMedia> photos) =>
                TelegramWithDelayAndPolly(x => x.SendMediaGroupAsync(photos, chatId, disableNotification: true))(tg);

            Task SendPhotoBatch(IEnumerable<(byte[], string)> photoSeq) => photoSeq
                .Filter(x => IsAcceptablePhoto(x.Item1))
                .Map((idx, t) =>
                    new InputMediaPhoto(new InputMedia(new MemoryStream(t.Item1), $"{idx}{t.Item2}"))
                )
                .Cast<IAlbumInputMedia>()
                .Apply(SendPhotosResiliently);

            var wr = await yandexMapsClient.Apply(YandexMapsWithPolly(x => x.Search(flat.Location, routeDestination)));
            await SendPhotoResiliently(wr.MapScreenshot, "map.jpeg");
            await wr.RoutesScreenshot.IterAsync(x => SendPhotoResiliently(x, "routes.jpeg"));

            try
            {
                var photosUrls = await onlinerClient.Apply(OnlinerByWithPolly(x => x.GetPhotosUrls(flat.Url)));
                await photosUrls.ToObservable().OrderedSelectMany(DownloadPhoto).Buffer(8).ToTaskChain(SendPhotoBatch);
            }
            catch (FlurlHttpException e) when (e.Call.HttpResponseMessage.StatusCode == HttpStatusCode.NotFound)
            {
                return unit;
            }

            state.LastScrapedEntityDate = flat.UpdatedAt;
            await State.Save(channelName, state);

            return unit;
        };

        static async Task RunBot(ChannelConfig cfg, ITelegramBotClient telegramBot, State state) {
            Console.WriteLine($"Init channel {cfg.Name}.");
            var apiLink = await OnlinerByApiLinkInterceptor.Intercept(cfg.OnlinerUrl);
            using var onlinerClient = new OnlinerByClient(apiLink);
            await using var yandexMapsClient = await YandexMapsClient.Launch();
            var routeDestination = Location.ParseYandexMapsUrl(cfg.RouteDestinationUrl);
            Console.WriteLine($"Channel {cfg.Name} is initialized.");
            var handleFlat = HandleFlat(telegramBot, onlinerClient, yandexMapsClient, routeDestination, new ChatId(cfg.TelegramChatId), cfg.Name, state);
            var isNotRoom = FlatType.Match(_: () => true, room: () => false);
            await GetOnlinerFlatUpdatesStream(onlinerClient, state.LastScrapedEntityDate)
                .Where(x => x.IsOwner)
                .Where(x => x.Type.Apply(isNotRoom))
                .ToTaskChain(handleFlat);
        }

        static async Task Main()
        {
            Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, "data");
            Console.OutputEncoding = Encoding.UTF8;

            var cfg = await RootConfig.Read();

            var telegramBot = new TelegramBotClient(cfg.TelegramBotToken);
            telegramBot.OnMessage += (x, y) =>
            {
                var msg = y.Message;
                Console.WriteLine($"{msg.Chat.Id} @{msg.Chat.Username}: {msg.Text}");
            };
            telegramBot.StartReceiving();

            await cfg.Channels.Filter(x => x.Enabled ?? true).Map(async channel =>
            {
                var state = (await State.Read(channel.Name)).IfNone(() => new State { LastScrapedEntityDate = DateTime.Now });
                await BotPollyPolicy.ExecuteAsync(() => RunBot(channel, telegramBot, state));
            }).Apply(Task.WhenAll);
        }
    }
}
