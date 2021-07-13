using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading.Tasks;
using OnlinerByFlatBot.Extensions;
using OnlinerByFlatBot.OnlinerBy;
using OnlinerByFlatBot.OnlinerBy.Model;
using OnlinerByFlatBot.YandexMaps;
using Flurl.Http;
using LanguageExt;
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
using System.Diagnostics;

namespace OnlinerByFlatBot
{
    static class Program
    {
        static Task<T> Delay<T>(Task<T> task, int delayInMs) =>
            task.Bind(r => Task.Delay(delayInMs).ToUnit().Map(_ => r));

        static Task<T> OnlinerDelay<T>(Task<T> task) =>
            Delay(task, Rnd.NextInt32(2000, 5000));

        static Task<T> TelegramDelay<T>(Task<T> task) =>
            Delay(task, 500);
	
        static IObservable<Arr<Flat>> GetOnlinerFlatScrapeStream(OnlinerByClient onlinerClient) =>
            RxHelpers.Generate(() => onlinerClient.GetLatestUpdate().Apply(OnlinerDelay));

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
            .Or<HttpRequestException>() // https://github.com/TelegramBots/Telegram.Bot/issues/891
            .WaitAndRetryAsync(6, ExpDelay, async (exception, delay) => {
                var retryAfter = TimeSpan.FromSeconds(
                    (exception as ApiRequestException)?.Parameters?.RetryAfter
                    ?? (
                        exception is HttpRequestException e && e.Message.Contains("Too Many Requests")
                        ? 20
                        : 0
                    )
                );
                var penalty = retryAfter - delay;
                if (penalty > TimeSpan.Zero) {
                    Log.Warning($"Telegram request penalty of {(int) penalty.TotalSeconds}s.");
                    await Task.Delay(penalty);
                }
            });

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
            .WaitAndRetryForeverAsync(sleepDurationProvider: _ => TimeSpan.FromSeconds(10), onRetry: (ex, _) => Log.Error(ex.Demystify().ToString()));

        static Func<ITelegramBotClient, Task<T>> TelegramWithDelayAndPolly<T>(Func<ITelegramBotClient, Task<T>> f) =>
            x => TelegramPollyPolicy.ExecuteAsync(() => f(x).Apply(TelegramDelay));

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
                flatWithNRooms: n => n + "-комнатная квартира"
            ));
            var txt = new [] {
                $"{(isNew ? "" : $"UP (объявление создано {timeSinceCreation.Humanize()} назад)")}",
                $"{type} за {flat.Price.Dollars}$",
                $"{flat.Address.TrimStart("Минск, ")}",
                flat.Url
            }.Filter(x => x.Length > 0).Apply(xs => string.Join(Environment.NewLine, xs));
            Log.Information($"[{channelName}] Processing flat:" + Environment.NewLine + txt);

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
                TelegramWithDelayAndPolly(x => x.SendMediaGroupAsync(chatId, photos, disableNotification: true))(tg);

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

            Log.Information($"[{channelName}] The flat is processed.");
            return unit;
        };

        static async Task RunBot(ChannelConfig cfg, ITelegramBotClient telegramBot, State state) {
            Log.Information($"[{cfg.Name}] Initilization.");
            var apiLink = await OnlinerByApiLinkInterceptor.Intercept(cfg.OnlinerUrl);
            using var onlinerClient = new OnlinerByClient(apiLink);
            await using var yandexMapsClient = await YandexMapsClient.Launch();
            var routeDestination = Location.ParseYandexMapsUrl(cfg.RouteDestinationUrl);
            Log.Information($"[{cfg.Name}] Initialized.");
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

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("log_.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var cfg = await RootConfig.Read();

            var telegramBot = new TelegramBotClient(cfg.TelegramBotToken);
            telegramBot.OnMessage += (x, y) =>
            {
                var msg = y.Message;
                Log.Information($"Incoming message from @{msg.Chat.Username} ({msg.Chat.Id}): {msg.Text}.");
            };
            telegramBot.StartReceiving();

            await cfg.Channels.Filter(x => x.Enabled ?? true).Map(async channel =>
            {
                var state = (await State.Read(channel.Name)).IfNone(() => new State { LastScrapedEntityDate = DateTime.Now.AddMinutes(-30) });
                await BotPollyPolicy.ExecuteAsync(() => RunBot(channel, telegramBot, state));
            }).Apply(Task.WhenAll);
        }
    }
}
