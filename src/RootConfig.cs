using System.IO;
using System.Threading.Tasks;
using LanguageExt;
using Newtonsoft.Json;

namespace OnlinerByFlatBot
{
    public sealed class ChannelConfig
    {
        public string Name { get; set; }
        public bool? Enabled { get; set; }
        public long TelegramChatId { get; set; }
        public string OnlinerUrl { get; set; }
        public string RouteDestinationUrl { get; set; }
    }

    public sealed class RootConfig
    {
        public string TelegramBotToken { get; set; }
        public ChannelConfig[] Channels { get; set; }

        public static Task<RootConfig> Read() =>
            File.ReadAllTextAsync("config.json").Map(JsonConvert.DeserializeObject<RootConfig>);
    }
}
