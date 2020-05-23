using LanguageExt;

namespace OnlinerByFlatBot.OnlinerBy {
    public sealed class OnlinerApiLink {
        public readonly string Url;
        public readonly string Cookies;

        public OnlinerApiLink(Some<string> url, Some<string> cookies) {
            Url = url;
            Cookies = cookies;
        }
    }
}
