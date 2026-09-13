# NCode v0.2 - СТАНДАРТНАЯ логика (как во всех языках):
#  1) задать <имя> <значение>
#     задать иван истина         -> иван = True
#     задать имя "Иван"          -> имя = "Иван" (текст только в кавычках)
#     задать копия возраст       -> копия = значение переменной возраст
#  2) вывести <что>
#     вывести иван               -> значение переменной иван
#     вывести "иван"             -> текст "иван"
import re
import sys

VARS = {}

PRINT_WORDS = {"вывести", "выведи", "напечатать", "напечатай", "печатать", "печатай", "показать", "покажи"}
SET_WORDS = {"задать", "задай"}

def parse_value(raw: str):
    """СТАНДАРТ:
    - "текст" -> строка-литерал
    - истина/ложь -> bool
    - число -> число
    - имя -> значение другой переменной (должна существовать)
    """
    raw = raw.strip()
    # строка-литерал в кавычках
    m = re.fullmatch(r'"([^"]*)"', raw)
    if m:
        return m.group(1)
    low = raw.lower()
    if low in ("истина", "истинно", "правда", "true"):
        return True
    if low in ("ложь", "ложно", "неправда", "false"):
        return False
    # числа: пробуем int, потом float
    try:
        return int(raw)
    except ValueError:
        pass
    try:
        return float(raw.replace(",", "."))
    except ValueError:
        pass
    # ссылка на переменную (голое слово)
    if re.fullmatch(r"[A-Za-zА-Яа-яЁё_][A-Za-zА-Яа-яЁё0-9_]*", raw):
        if raw not in VARS:
            raise NameError(f'нет такой переменной: {raw}. Текст надо в кавычках: "{raw}"')
        return VARS[raw]
    raise SyntaxError(f'не понимаю значение: {raw}. Текст — в кавычках: "{raw}"')

def fmt(v):
    if v is True:
        return "истина"
    if v is False:
        return "ложь"
    return str(v)

def run_line(line: str, num: int):
    line = line.strip()
    if not line or line.startswith("#") or line.startswith("//"):
        return
    # первая команда = первое слово
    parts = line.split(maxsplit=1)
    cmd = parts[0].lower()
    rest = parts[1] if len(parts) > 1 else ""

    if cmd in SET_WORDS:
        # задать <имя> <значение...>
        m = re.match(r"(\S+)\s+(.+)", rest)
        if not m:
            raise SyntaxError(f"строка {num}: надо так -> задать иван истина")
        name, raw_val = m.group(1), m.group(2).strip()
        if not re.fullmatch(r"[A-Za-zА-Яа-яЁё_][A-Za-zА-Яа-яЁё0-9_]*", name):
            raise SyntaxError(f"строка {num}: плохое имя '{name}'")
        VARS[name] = parse_value(raw_val)
    elif cmd in PRINT_WORDS:
        arg = rest.strip()
        if not arg:
            raise SyntaxError(f"строка {num}: что вывести? пример: вывести иван")
        # СТАНДАРТ: "текст" -> литерал, голое -> переменная / число / bool
        m = re.fullmatch(r'"([^"]*)"', arg)
        if m:
            print(m.group(1))
        else:
            # голое должно быть одним словом
            if len(arg.split()) > 1:
                raise SyntaxError(f'строка {num}: текст с пробелами надо в кавычках: "{arg}"')
            print(fmt(parse_value(arg)))
    else:
        raise SyntaxError(f"строка {num}: не знаю команду '{parts[0]}' (знаю: задать, вывести)")

def run_file(path: str):
    with open(path, encoding="utf-8") as f:
        for i, line in enumerate(f, 1):
            run_line(line, i)

if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else "test.ncode"
    run_file(path)
