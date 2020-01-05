using System;

namespace OnlinerByFlatBot.OnlinerBy.Model
{
    public static class FlatType
    {
        public static Func<string, T> Match<T>(
            Func<T> _,
            Func<T> room = null,
            Func<int, T> flatWithNRooms = null
        ) => type =>
        {
            if (_ == null) throw new ArgumentNullException(nameof(_));
            switch (type)
            {
                case "room" when room != null:
                    return room();
                case string s when s == "1_room" && flatWithNRooms != null:
                    return flatWithNRooms(1);
                case string s when s.EndsWith("_rooms") && flatWithNRooms != null:
                    return flatWithNRooms(int.Parse(s[0].ToString()));
                default:
                    return _();
            }
        };
    }
}