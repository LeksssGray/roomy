# Roomy — русская версия

Русификация [Roomy](https://github.com/krezio/roomy) версии 0.1.2.
Автор оригинального приложения — krezio; лицензия MIT сохранена.
Русская версия — А. О. Серов. Личный знак АОС перенесён из проекта АЛЁНА:
SVG-оригинал находится в `docs/brand/aos-dark.svg`, WPF-вариант — в
`src/Roomy/Controls/DeveloperMark.xaml`. Знак показан в шапке приложения,
настройках и карточке результата. Геометрия и палитра исходного знака сохранены.

Переведены экраны приложения, настройки, меню значка в области уведомлений,
уведомления Windows, категории и пояснения очистки, предупреждения и карточка
результата. Размеры и относительные даты отображаются по-русски. Имена файлов,
системные пути и названия сторонних программ сохраняются.

## Запуск

Русская portable-сборка: `dist/ru-brand/Roomy.exe`.
Первоначальный официальный английский релиз: `dist/Roomy-portable.exe`.
Файлы сборок не хранятся в Git.

Сканирование ничего не удаляет. Очистка запускается только кнопкой пользователя;
удалённые при очистке файлы не попадают в корзину.

## Разработка

Нужны Windows 10 версии 2004 или новее либо Windows 11 и .NET 10 SDK.
На этом компьютере SDK установлен глобально в `C:/Program Files/dotnet`.

```powershell
dotnet run --project src/Roomy
dotnet test tests/Roomy.Tests -c Release
dotnet run --project tests/Roomy.DesktopChecks -c Release
dotnet publish src/Roomy -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/ru-brand
```

Проверка внешнего вида на вымышленных данных, без очистки реальных файлов:

```powershell
dist/ru-brand/Roomy.exe --shots artifacts/shots-ru
dotnet run --project tests/Roomy.DesktopChecks -c Release -- --snapshot artifacts/settings-ru.png
```

Исходная документация и описание возможностей: [README.md](README.md).
