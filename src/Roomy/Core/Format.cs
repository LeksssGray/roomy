namespace Roomy.Core;

public static class Format
{
    static readonly string[] Units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];

    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "0 Б";
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < Units.Length - 1) { v /= 1024; i++; }
        string n = i == 0 || v >= 100 ? v.ToString("0") : v >= 10 ? v.ToString("0.#") : v.ToString("0.##");
        return $"{n} {Units[i]}";
    }

    public static string Plural(int count, string one, string few, string many)
    {
        int n = Math.Abs(count) % 100;
        if (n is >= 11 and <= 14) return many;
        return (n % 10) switch { 1 => one, >= 2 and <= 4 => few, _ => many };
    }

    public static string Ago(DateTime when)
    {
        var d = DateTime.Now - when;
        if (d.TotalDays < 1) return "сегодня";
        if (d.TotalDays < 2) return "вчера";
        if (d.TotalDays < 14)
        {
            int days = (int)d.TotalDays;
            return $"{days} {Plural(days, "день", "дня", "дней")} назад";
        }
        if (d.TotalDays < 60)
        {
            int weeks = (int)(d.TotalDays / 7);
            return $"{weeks} {Plural(weeks, "неделю", "недели", "недель")} назад";
        }
        if (d.TotalDays < 365)
        {
            int months = (int)(d.TotalDays / 30);
            return $"{months} {Plural(months, "месяц", "месяца", "месяцев")} назад";
        }
        int years = (int)(d.TotalDays / 365);
        return $"{years} {Plural(years, "год", "года", "лет")} назад";
    }
}
