using System;
using System.IO;
using System.Threading.Tasks;
using LanguageExt;
using Newtonsoft.Json;
using static LanguageExt.Prelude;

namespace OnlinerByFlatBot
{
    public sealed class State
    {
        public DateTime LastScrapedEntityDate { get; set; }

        static string FileName(string channelName) => $"state_{channelName}.json";

        public static Task<Option<State>> Read(string channelName) => !File.Exists(FileName(channelName))
            ? Option<State>.None.AsTask()
            : File.ReadAllTextAsync(FileName(channelName)).Map(JsonConvert.DeserializeObject<State>).Map(Some);

        public static Task Save(string channelName, Some<State> state) => state.Value
            .Apply(s => JsonConvert.SerializeObject(s, Formatting.Indented))
            .Apply(json => File.WriteAllTextAsync(FileName(channelName), json));
    }
}
