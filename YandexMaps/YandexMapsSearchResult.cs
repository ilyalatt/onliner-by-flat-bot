using LanguageExt;

namespace OnlinerByFlatBot.YandexMaps
{
    public sealed class YandexMapsSearchResult
    {
        public readonly Option<byte[]> RoutesScreenshot;
        public readonly byte[] MapScreenshot;

        public YandexMapsSearchResult(Option<byte[]> routesScreenshot, byte[] mapScreenshot)
        {
            RoutesScreenshot = routesScreenshot;
            MapScreenshot = mapScreenshot;
        }
    }
}
