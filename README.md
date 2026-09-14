# Ncode

Русскоязычный язык для игр и программ. Пиши по-русски — рантайм Windows (WinForms окно + звук) и Android (MediaPlayer + Toast), редактор — только Windows.

```ncode
задать имя "мир"
вывести "привет, " + имя

создать список фрукты
добавить в фрукты "яблоко"
для каждого ф в фрукты то
  вывести ф
конец
```

## Быстрый старт

1. Установи [.NET 8 SDK](https://dotnet.microsoft.com/download) (+ `dotnet workload install android` только для Android-рантайма).
2. Собери рантайм: `dotnet build Ncode.sln` (Windows + headless) — редактор на Android не портируется, только `Ncode` + `Ncode.Core`
3. Запусти редактор (только Windows): `editor.bat` (или `dotnet Ncode.Editor/bin/Debug/net8.0/Ncode.Editor.dll`)
4. Или запусти файл напрямую (только Windows): `ncode.bat игра.ncode` / `dotnet Ncode/bin/Debug/net8.0-windows/Ncode.dll игра.ncode` (с окном) / `dotnet Ncode/bin/Debug/net8.0/Ncode.dll игра.ncode` (headless для тестов)

## Документация

Полный справочник — [`документация.txt`](документация.txt): все команды, выражения, примеры, ошибки и что они значат.

## Тесты

```
dotnet test
dotnet test Ncode.Tests
```

20 golden-тестов гоняют интерпретатор через `Ncode.dll` без окна/звука.

## Безопасность: нет песочницы

Язык умеет `записать/удалить/переименовать/скопировать файл`, `список файлов`, `получить/отправить` в сеть — пути не ограничены папкой проекта. Запускай только свои `.ncode`-файлы или от доверенных авторов. Чужой скрипт из интернета может менять файлы и ходить в сеть. Если делишься игрой — отдавай исходники (AGPL обязывает).

## Сборка релиза

```
dotnet publish Ncode -c Release -r win-x64 --self-contained
dotnet publish Ncode.Editor -c Release -r win-x64 --self-contained
```

## Лицензия

[GNU AGPL v3](LICENSE). Хедеры `SPDX-License-Identifier: AGPL-3.0-or-later` во всех исходниках.
