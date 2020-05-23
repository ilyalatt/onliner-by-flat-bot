using System.IO;
using System.Threading.Tasks;
using LanguageExt;
using Newtonsoft.Json;

namespace OnlinerByFlatBot
{
    public sealed class OnlinerConfig
    {
        public string SearchUrl { get; set; }
        public string Cookies { get; set; }
    }

    public sealed class TgBotConfig
    {
        public int ChatId { get; set; }
    }

    public sealed class RouteConfig
    {
        public Location Location { get; set; }
    }

    public sealed class ChannelConfig
    {
        public string Name { get; set; }
        public bool? Enabled { get; set; }
        public int TelegramChatId { get; set; }
        public OnlinerConfig Onliner { get; set; }
        public RouteConfig Route { get; set; }
    }

    public sealed class RootConfig
    {
        public string TelegramBotToken { get; set; }
        public ChannelConfig[] Channels { get; set; }

        public static Task<RootConfig> Read() =>
            File.ReadAllTextAsync("config.json").Map(JsonConvert.DeserializeObject<RootConfig>);
    }
}
