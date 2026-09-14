// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;

namespace Ncode.Editor;

public sealed class NcodeHighlighting : IHighlightingDefinition
{
    public string Name => "NCode";
    public HighlightingRuleSet MainRuleSet { get; } = new HighlightingRuleSet();
    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
    public HighlightingRuleSet? GetNamedRuleSet(string name) => null;
    public HighlightingColor GetNamedColor(string name) => new HighlightingColor();
    public IEnumerable<HighlightingColor> NamedHighlightingColors => [];

    private static HighlightingColor Paint(string hex, bool bold = false, bool italic = false)
    {
        return new HighlightingColor
        {
            Foreground = new SimpleHighlightingBrush(Color.Parse(hex)),
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal
        };
    }

    private static string Words(params string[] words)
    {
        return "(?<![\\p{L}\\p{N}_])(?:" + string.Join("|", words) + ")(?![\\p{L}\\p{N}_])";
    }

    public NcodeHighlighting()
    {
        var comment = Paint("#87867F", italic: true);
        var str = Paint("#98A886");
        var num = Paint("#E59560");
        var keyword = Paint("#C96442", bold: true);
        var cmd = Paint("#DEB368");
        var op = Paint("#D97757");

        string[] keywords =
        [
            "если", "эсли", "то", "тогда", "иначе", "конец",
            "повтори", "повторить", "повторять", "пока", "покуда", "всегда", "вечно",
            "для", "каждого", "каждый", "попробовать", "поймать", "попробуй", "поймай",
            "как", "только", "когда", "получено", "будет", "чтобы", "каждые", "каждый",
            "при", "запуске", "старте", "нажатии", "нажатия", "стоп", "дальше",
            "истина", "истинно", "правда", "ложь", "ложно", "неправда", "да", "нет",
            "не", "и", "или", "пробел", "время", "дата", "миллисекунды", "вернуть",
            "нажата", "клавиша", "кнопка", "образ", "свойство", "объект", "обьект", "размер", "прозрачность",
            "таблицу", "таблицы", "таблица", "ключи", "значения", "ключ",
            "статичный", "динамичный", "статический", "динамический",
            "столкновении", "выходе", "покое", "экране"
        ];

        string[] commands =
        [
            "задать", "задай", "изменить", "поменять",
            "вывести", "выведи", "напечатать", "напечатай", "печатать", "печатай",
            "показать", "покажи", "спросить", "спроси", "сохранить",
            "вещать", "вешать", "всем", "создать", "создай", "открыть", "окно", "сцену", "скрипт", "скрипты", "запустить",
            "нарисовать", "нарисуй", "присвоить", "присвой",
            "остановить", "останови", "прервать", "прерви", "продолжить", "продолжи", "пауза", "приостановить", "возобновить", "возобнови", "громкость",
            "выйти", "выход", "закончить", "завершить",
            "ждать", "жди", "каждые", "перемешать", "перемешай", "вставить", "вставь", "скопировать", "скопируй",
            "добавить", "добавь", "взять", "удалить", "удали", "переименовать",
            "очистить", "очисти", "разделить", "раздели", "склеить", "склей",
            "записать", "запиши", "прочитать", "прочитай", "есть",
            "воспроизвести", "воспроизведи", "играть", "играй", "сыграть", "сыграй", "звук",
            "сообщение", "ошибку", "получить", "отправить", "POST", "PUT", "PATCH", "DELETE",
            "подключить", "подключи", "длина", "найти", "заменить", "срез",
            "верхний", "нижний", "случайное", "корень", "модуль", "округлить",
            "пол", "потолок", "синус", "косинус", "тангенс", "логарифм",
            "максимум", "минимум", "степень", "остаток",
            "заглавные", "строчные", "обрезать", "содержит", "начинается", "заканчивается",
            "тип", "json", "расширение", "имя",
            "тяжесть", "движения", "движение", "ширина_окна", "высота_окна", "окно_ширина", "окно_высота",
            "скорость", "ускорение", "переместить", "перемести", "угол", "повернуть", "поверни",
            "угловую", "силу", "взрыв", "притянуть", "притяни", "массу", "масса", "демпфирование",
            "упругость", "трение", "лимит", "заморозить", "заморозь", "разморозить", "разморозь",
            "ограничение", "прикрепить", "прикрепи", "открепить", "открепи", "слой", "маска",
            "коллизий", "коллизии", "игнорировать", "восстановить", "граница", "отскочить", "отскочи",
            "плавно", "расстояние", "проверить", "столкновение", "направление",
            "шаг", "раз", "от", "до", "по", "файл", "папку", "список", "минуту", "минуты", "минут",
            "секунду", "секунды", "секунд", "час", "часа", "часов"
        ];

        MainRuleSet.Rules.Add(new HighlightingRule { Regex = new Regex("(//|#).*$"), Color = comment });
        MainRuleSet.Rules.Add(new HighlightingRule { Regex = new Regex("\"[^\"]*\""), Color = str });
        MainRuleSet.Rules.Add(new HighlightingRule { Regex = new Regex("\\b\\d+([.,]\\d+)?\\b"), Color = num });
        MainRuleSet.Rules.Add(new HighlightingRule { Regex = new Regex(Words(keywords), RegexOptions.IgnoreCase), Color = keyword });
        MainRuleSet.Rules.Add(new HighlightingRule { Regex = new Regex(Words(commands), RegexOptions.IgnoreCase), Color = cmd });
        MainRuleSet.Rules.Add(new HighlightingRule { Regex = new Regex("\\.\\.|>=|<=|!=|==|&&|\\|\\||[+\\-*/%=><!^]"), Color = op });
    }
}
