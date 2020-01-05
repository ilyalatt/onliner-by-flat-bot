using System;
using LanguageExt;

namespace OnlinerByFlatBot.OnlinerBy.Model
{
    public sealed class Flat : Record<Flat>
    {
        public readonly int Id;
        public readonly string Url;
        public readonly string PhotoUrl;
        public readonly string Type;
        public readonly string Address;
        public readonly Location Location;
        public readonly bool IsOwner;
        public readonly UsdPrice Price;
        public readonly DateTime CreatedAt;
        public readonly DateTime UpdatedAt;

        public Flat(int id, Some<string> url, Some<string> photoUrl, Some<string> type, Some<string> address, Some<Location> location, bool isOwner, Some<UsdPrice> price, DateTime createdAt, DateTime updatedAt)
        {
            Id = id;
            Url = url;
            PhotoUrl = photoUrl;
            Type = type;
            Address = address;
            Location = location;
            IsOwner = isOwner;
            Price = price;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }
    }
}