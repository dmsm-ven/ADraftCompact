# Services

- `DotaAssetsDownloader` скачивает изображения героев и способностей, а также JSON-справочники OpenDota.
- Ресурсы сохраняются в `%LocalAppData%\AbilityDraftOverlay\Assets`.
- Загрузчик пропускает уже существующие файлы и может использоваться повторно для дозагрузки.
- Сетевые источники: OpenDota constants API и публичный Steam CDN. При ошибке сети загрузку можно запустить повторно.
- `IconIndex` строит и кэширует индекс дескрипторов иконок героев и способностей (`Cache\icons.index`).
- `DraftLayoutProvider` читает раскладку слотов (`Layouts\ability_draft_1920x1080.json` или пользовательский `layout.1920x1080.json`).
- `ScreenshotRecognitionService` распознаёт кадр по слотам раскладки и сохраняет отладочное изображение с рамками слотов.
- `HeroNameReader` читает строку с именем героя встроенным OCR Windows, `HeroNameMatcher` нечётко сопоставляет её со списком героев.
- `GameWindowCapture` снимает только окно игры (процесс `dota2`, любой монитор), пропуская кадр, если окно свёрнуто или перекрыто.
- `DraftSessionTracker` ведёт сессии (GUID + дата) и порядок выбора у каждого игрока; `SessionStore` сохраняет их в `Sessions\*.json`.
- `AutoTrackingService` по таймеру: снимок окна → распознавание → трекер.
- `DraftReportFormatter` формирует текстовый отчёт по игрокам.
