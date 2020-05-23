using System;
using LanguageExt;

namespace OnlinerByFlatBot.OnlinerBy.Model
{
    public sealed class UsdPrice : Record<UsdPrice> 
    {
        public readonly int Dollars;
        public readonly int Cents;

        public UsdPrice(int dollars, int cents)
        {
            if (dollars < 0) throw new ArgumentException("dollars < 0", nameof(dollars));
            if (cents < 0) throw new ArgumentException("cents < 0", nameof(cents));
            if (cents > 99) throw new ArgumentException("cents > 99", nameof(cents));
            
            Dollars = dollars;
            Cents = cents;
        }

        public static UsdPrice Parse(string s)
        {
            var spl = s.Split('.', 2);
            return new UsdPrice(int.Parse(spl[0]), int.Parse(spl[1]));
        }

        public override string ToString() => $"{Dollars}.{Cents:00}";
    }
}