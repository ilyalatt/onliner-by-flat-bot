using LanguageExt;
using NodaTime;

namespace OnlinerByFlatBot
{
    public static class TimeHumanizer
    {
        // http://www.sql.ru/forum/actualutils.aspx?action=gotomsg&tid=524746&msg=5273302
        static string Num(long days, string s1, string s2, string s3) =>
            days >= 10 && days / 10 % 10 == 1 ? s1
            : (days % 10).Apply(x => x == 0 || x >= 5) ? s1
            : (days % 10).Apply(x => x == 2 || x == 3 || x == 4) ? s2
            : s3;
        
        public static string Years(long n) => n + " " + Num(n, "лет", "года", "год");
        public static string Months(long n) => n + " " + Num(n, "месяцев", "месяца", "месяц");
        public static string Days(long n) => n + " " + Num(n, "дней", "дня", "день");
        public static string Hours(long n) => n + " " + Num(n, "часов", "часа", "час");
        public static string Minutes(long n) => n + " " + Num(n, "минут", "минуты", "минуту");
        public static string Seconds(long n) => n + " " + Num(n, "секунд", "секунды", "секунду");

        public static string Humanize(this Period p) =>
            p.Seconds == 0 ? "менее секунды" :
            p.Minutes == 0 ? Seconds(p.Seconds):
            p.Hours == 0 ? Minutes(p.Minutes) :
            p.Days == 0 ? Hours(p.Hours) :
            p.Months == 0 ? Days(p.Days) :
            p.Years == 0 ? Months(p.Months) + " " + Days(p.Days) :
            Years(p.Years) + " " + Months(p.Months);
    }
}