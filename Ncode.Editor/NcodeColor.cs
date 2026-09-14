using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ncode.Editor;

public sealed record NcodeSpan(string Text, string Color, bool Bold, bool Italic);

public static class NcodeColor
{
    private static readonly string[] Keywords =
    [
        "если", "эсли", "то", "тогда", "иначе", "конец",
        "повтори", "повторить", "повторять", "пока", "покуда", "всегда", "вечно",
        "для", "каждого", "каждый", "попробовать", "поймать", "попробуй", "поймай",
        "как", "только", "когда", "получено", "будет", "чтобы", "каждые", "каждый",
        "при", "запуске", "старте", "стоп", "дальше",
        "истина", "истинно", "правда", "ложь", "ложно", "неправда", "да", "нет",
        "не", "и", "или", "пробел", "время", "вернуть"
    ];

    private static readonly string[] Commands =
    [
        "задать", "задай", "изменить", "поменять",
        "вывести", "выведи", "напечатать", "напечатай", "печатать", "печатай",
        "показать", "покажи", "спросить", "спроси", "сохранить",
        "вещать", "вешать", "всем", "создать", "создай", "открыть", "окно", "сцену", "запустить",
        "остановить", "останови", "прервать", "прерви", "продолжить", "продолжи",
        "выйти", "выход", "закончить", "завершить",
        "ждать", "жди", "каждые", "перемешать", "перемешай", "вставить", "вставь",
        "добавить", "добавь", "взять", "удалить", "удали",
        "очистить", "очисти", "разделить", "раздели", "склеить", "склей",
        "записать", "запиши", "прочитать", "прочитай", "есть",
        "подключить", "подключи", "длина", "найти", "заменить", "срез",
        "верхний", "нижний", "случайное", "корень", "модуль", "округлить",
        "шаг", "раз", "от", "до", "по", "файл", "папку", "список", "минуту", "минуты", "минут",
        "секунду", "секунды", "секунд", "час", "часа", "часов"
    ];

    private static string Words(string[] words)
    {
        return "(?<![\\p{L}\\p{N}_])(?:" + string.Join("|", words) + ")(?![\\p{L}\\p{N}_])";
    }

    private static readonly (Regex re, string color, bool bold, bool italic)[] Rules =
    [
        (new Regex("(//|#).*$"), "#6C7086", false, true),
        (new Regex("\"[^\"]*\""), "#A6E3A1", false, false),
        (new Regex("\\b\\d+([.,]\\d+)?\\b"), "#FAB387", false, false),
        (new Regex(Words(Keywords), RegexOptions.IgnoreCase), "#CBA6F7", true, false),
        (new Regex(Words(Commands), RegexOptions.IgnoreCase), "#89DCEB", false, false),
        (new Regex("\\.\\.|>=|<=|!=|==|&&|\\|\\||[+\\-*/%=><!^]"), "#F38BA8", false, false),
    ];

    public static List<NcodeSpan> ColorizeLine(string line)
    {
        var paint = new (string color, bool bold, bool italic)[line.Length];
        for (int i = 0; i < paint.Length; i++)
            paint[i] = ("#CDD6F4", false, false);
        foreach (var (re, color, bold, italic) in Rules)
        {
            foreach (Match m in re.Matches(line))
            {
                for (int i = m.Index; i < m.Index + m.Length && i < paint.Length; i++)
                    paint[i] = (color, bold, italic);
            }
        }
        var out_ = new List<NcodeSpan>();
        int j = 0;
        while (j < line.Length)
        {
            int k = j + 1;
            while (k < line.Length && paint[k] == paint[j])
                k++;
            out_.Add(new NcodeSpan(line[j..k], paint[j].color, paint[j].bold, paint[j].italic));
            j = k;
        }
        return out_;
    }
}
