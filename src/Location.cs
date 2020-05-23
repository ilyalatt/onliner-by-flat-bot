using System;
using System.Globalization;
using System.Linq;
using LanguageExt;

namespace OnlinerByFlatBot
{
    public sealed class Location : Record<Location>
    {
        public readonly double Latitude;
        public readonly double Longitude;

        public Location(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }

        static readonly NumberFormatInfo NumFormat =
            new NumberFormatInfo { NumberDecimalSeparator = "." };
        public override string ToString() =>
            $"{Latitude.ToString(NumFormat)},{Longitude.ToString(NumFormat)}";

        public static Location ParseYandexMapsUrl(string url) =>
            url
            .Split('?', '&')
            .Map(x => x.Split('=').Map(Uri.UnescapeDataString).ToList())
            .Find(x => x[0] == "ll")
            .Match(
                x => {
                    var t = x[1].Split(',');
                    var longitude = double.Parse(t[0], NumFormat);
                    var latitude = double.Parse(t[1], NumFormat);
                    return new Location(latitude, longitude);
                },
                () => throw new ArgumentException($"Can not find 'll' query parameter in '{url}'.")
            );
    }
}