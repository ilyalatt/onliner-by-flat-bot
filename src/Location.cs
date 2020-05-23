using System.Globalization;
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
    }
}