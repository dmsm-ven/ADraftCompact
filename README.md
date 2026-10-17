# Ability Draft Overlay

Начальный WPF-каркас для ручного пошагового анализа Dota 2 Ability Draft без AI и OCR.

## Что реализовано на текущем этапе

- .NET 10 WPF, `.slnx`, DI через `Microsoft.Extensions.Hosting`, MVVM через CommunityToolkit.Mvvm.
- Одновременно запускается только один экземпляр приложения.
- Главное окно без окна настроек и без переключения тем.
- Кнопка **«Выбрать следующий кадр»** открывает локальный скриншот и показывает его в окне.
- Список `ListBox` показывает журнал событий; пока автоматически добавляется только событие выбора кадра.
- Кнопка **«Загрузить ресурсы»** скачивает справочники OpenDota и портреты и иконки героев, изображения способностей с публичного Steam CDN.
- Загрузка инкрементальная: существующие непустые файлы не скачиваются повторно.

## Запуск

Требуются Windows и .NET 10 SDK.

```powershell
dotnet restore .\AbilityDraftOverlay.slnx
dotnet build .\AbilityDraftOverlay.slnx
dotnet run --project .\src\AbilityDraftOverlay\AbilityDraftOverlay.csproj
```

## Где хранятся ресурсы

`%LocalAppData%\AbilityDraftOverlay\Assets`

Внутри будут `heroes` (портреты и иконки), `abilities` и `data`. Способности скачиваются в современном варианте и в двух legacy-размерах (`_lg`, `_md`), поскольку их внешний вид может различаться от варианта, показанного в игровом драфте.

## Что пока не реализовано

- Автоматический таймер захвата экрана.
- Распознавание иконок и определение игрока/порядка выбора.
- Прозрачный игровой overlay.
- SQLite-хранение событий.

## Источники и примечания

- [OpenDota constants](https://github.com/odota/dotaconstants) — JSON-метаданные героев и способностей.
- [Steam CDN](https://cdn.cloudflare.steamstatic.com/apps/dota2/images/) — публичные изображения Dota 2.
- [Dota 2 Assets Downloader](https://github.com/CaptchaQ/dota2-assets-downloader) — альтернативный PowerShell-загрузчик, если позже понадобятся дополнительные категории ресурсов.

Изображения и игровые ресурсы принадлежат их правообладателям. Совпадение скачанных иконок с изображениями на конкретном экране Ability Draft нужно будет проверить на реальных скриншотах.
