# Ability Draft Overlay

Каркас WPF-приложения для локального overlay и анализа драфта Dota 2 Ability Draft.

## Текущий этап

Это именно стартовый каркас: DI/Host, MVVM, главное окно, окно настроек, базовые модели и сервисы-заглушки. Захват экрана, распознавание изображений и игровой overlay пока не реализованы.

## Требования

- Windows
- .NET 10 SDK
- Visual Studio 2026 или `dotnet` CLI с поддержкой WPF

## Запуск

Откройте `AbilityDraftOverlay.slnx` в Visual Studio либо выполните:

```powershell
dotnet restore .\AbilityDraftOverlay.slnx
dotnet build .\AbilityDraftOverlay.slnx
dotnet run --project .\src\AbilityDraftOverlay\AbilityDraftOverlay.csproj
```

## Архитектура

- `Models/` — модели настроек и событий драфта.
- `ViewModels/` — MVVM-состояние окон.
- `Views/` — главное окно и окно настроек.
- `Services/` — настройки, управление экземпляром приложения и будущие сервисы захвата/распознавания.
- `Data/` — заготовка под SQLite/Dapper.
- `Resources/` — место для стилей и ресурсов интерфейса.

## Следующие этапы

1. Добавить выбор и сохранение темы/настроек.
2. Сделать тестовый экран для открытия скриншота 1920×1080 и настройки прямоугольников.
3. Добавить источник иконок/метаданных Dota 2.
4. Реализовать распознавание слотов без AI.
5. Реализовать прозрачное overlay-окно.

## Источники игровых ресурсов

Рассматриваемый загрузчик: https://github.com/CaptchaQ/dota2-assets-downloader  
Метаданные Dota 2: https://github.com/odota/dotaconstants

Игровые изображения принадлежат Valve. Перед распространением приложения с ресурсами следует проверить условия их использования.
