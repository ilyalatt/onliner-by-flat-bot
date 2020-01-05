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
            .WaitAndRetryAsync(4, ExpDelay);

        static readonly AsyncRetryPolicy OnlinerByPollyPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<OperationCanceledException>()
            .Or<FlurlHttpException>(x => x.Call.HttpResponseMessage.StatusCode != HttpStatusCode.NotFound)
            .WaitAndRetryAsync(4, ExpDelay);

        static readonly AsyncRetryPolicy YandexMapsPollyPolicy = Policy
            .Handle<PuppeteerException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(4, ExpDelay);

        static readonly AsyncRetryPolicy BotPollyPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryForeverAsync(sleepDurationProvider: _ => TimeSpan.FromSeconds(10), onRetry: (ex, _) => Console.WriteLine(ex));

        static Func<ITelegramBotClient, Task<T>> TelegramWithPolly<T>(Func<ITelegramBotClient, Task<T>> f) =>
            x => TelegramPollyPolicy.ExecuteAsync(() => f(x));

        static Func<OnlinerByClient, Task<T>> OnlinerByWithPolly<T>(Func<OnlinerByClient, Task<T>> f) =>
            x => OnlinerByPollyPolicy.ExecuteAsync(() => f(x));

        static Func<YandexMapsClient, Task<T>> YandexMapsWithPolly<T>(Func<YandexMapsClient, Task<T>> f) =>
            x => YandexMapsPollyPolicy.ExecuteAsync(() => f(x));

        static Func<Flat, Task<Unit>> HandleFlat(
            ITelegramBotClient tg,
            OnlinerByClient onlinerClient,
            YandexMapsClient yandexMapsClient,
            RouteConfig routeConfig,
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
            Task SendPhotoResiliently(
                byte[] photo,
                string fileName,
                string caption = null,
                bool disableNotification = true
            ) =>
                TelegramWithPolly(x => x.SendPhotoAsync(
                    chatId: chatId,
                    photo: new InputOnlineFile(
                        new MemoryStream(photo),
                        fileName
                    ),
                    caption: caption,
                    disableNotification: disableNotification
                ))(tg);

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
                TelegramWithPolly(x => x.SendMediaGroupAsync(photos, chatId, disableNotification: true))(tg);

            Task SendPhotoBatch(IEnumerable<(byte[], string)> photoSeq) =>
                photoSeq.Map((idx, t) =>
                    new InputMediaPhoto(new InputMedia(new MemoryStream(t.Item1), $"{idx}{t.Item2}"))
                ).Cast<IAlbumInputMedia>()
                .Apply(SendPhotosResiliently);

            var wr = await yandexMapsClient.Apply(YandexMapsWithPolly(x => x.Search(flat.Location, routeConfig.Location)));
            await SendPhotoResiliently(wr.MapScreenshot, "map.png");
            await wr.RoutesScreenshot.IterAsync(x => SendPhotoResiliently(x, "routes.png"));

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

        static async Task RunBot(ChannelConfig cfg, ITelegramBotClient telegramBot, State state)
        {
            using var onlinerClient = new OnlinerByClient(cfg.Onliner);
            using var yandexMapsClient = await YandexMapsClient.Launch();
            var handleFlat = HandleFlat(telegramBot, onlinerClient, yandexMapsClient, cfg.Route, new ChatId(cfg.TelegramChatId), cfg.Name, state);
            var isNotRoom = FlatType.Match(_: () => true, room: () => false);
            await GetOnlinerFlatUpdatesStream(onlinerClient, state.LastScrapedEntityDate)
                .Where(x => x.IsOwner)
                .Where(x => x.Type.Apply(isNotRoom))
                .ToTaskChain(handleFlat);
        }

        static async Task Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            var cfg = await RootConfig.Read();

            var telegramBot = new TelegramBotClient(cfg.TelegramBotToken);
            telegramBot.OnMessage += (x, y) =>
            {
                var msg = y.Message;
                Console.WriteLine($"{msg.Chat.Id} @{msg.Chat.Username}: {msg.Text}");
            };
            telegramBot.StartReceiving();

            await cfg.Channels.Filter(x => x.Enabled).Map(async channel =>
            {
                var state = (await State.Read(channel.Name)).IfNone(() => new State { LastScrapedEntityDate = DateTime.Now });
                await BotPollyPolicy.ExecuteAsync(() => RunBot(channel, telegramBot, state));
            }).Apply(Task.WhenAll);
        }
    }
}
