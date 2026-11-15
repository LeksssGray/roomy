using System.Globalization;
using Roomy.Core;

namespace Roomy.Tests;

public sealed class FormatTests
{
    [Theory]
    [InlineData(1, "день")]
    [InlineData(2, "дня")]
    [InlineData(5, "дней")]
    [InlineData(11, "дней")]
    [InlineData(12, "дней")]
    [InlineData(14, "дней")]
    [InlineData(21, "день")]
    [InlineData(22, "дня")]
    [InlineData(25, "дней")]
    [InlineData(111, "дней")]
    public void RussianPluralFormsHandleTeensAndCompoundNumbers(int count, string expected) =>
        Assert.Equal(expected, Format.Plural(count, "день", "дня", "дней"));

    [Theory]
    [InlineData(0, "сегодня")]
    [InlineData(1, "вчера")]
    [InlineData(2, "2 дня назад")]
    [InlineData(5, "5 дней назад")]
    [InlineData(11, "11 дней назад")]
    [InlineData(15, "2 недели назад")]
    [InlineData(35, "5 недель назад")]
    [InlineData(91, "3 месяца назад")]
    [InlineData(151, "5 месяцев назад")]
    [InlineData(366, "1 год назад")]
    [InlineData(731, "2 года назад")]
    [InlineData(1826, "5 лет назад")]
    public void RelativeDatesUseRussianWords(int days, string expected) =>
        Assert.Equal(expected, Format.Ago(DateTime.Now.AddDays(-days).AddMinutes(-5)));

    [Fact]
    public void ByteSizesUseRussianUnitsAndDecimalSeparator()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            Assert.Equal("0 Б", Format.Bytes(0));
            Assert.Equal("1 КБ", Format.Bytes(1024));
            Assert.Equal("1,5 МБ", Format.Bytes(1572864));
            Assert.Equal("1 ГБ", Format.Bytes(1L << 30));
            Assert.Equal("1 ТБ", Format.Bytes(1L << 40));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
