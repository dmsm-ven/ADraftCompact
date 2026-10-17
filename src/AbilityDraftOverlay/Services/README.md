# Services

- `DotaAssetsDownloader` скачивает изображения героев и способностей, а также JSON-справочники OpenDota.
- Ресурсы сохраняются в `%LocalAppData%\AbilityDraftOverlay\Assets`.
- Загрузчик пропускает уже существующие файлы и может использоваться повторно для дозагрузки.
- Сетевые источники: OpenDota constants API и публичный Steam CDN. При ошибке сети загрузку можно запустить повторно.
- `IconIndex` строит и кэширует индекс дескрипторов иконок героев и способностей (`Cache\icons.index`).
- `DraftLayoutProvider` читает раскладку слотов (`Layouts\ability_draft_1920x1080.json` или пользовательский `layout.1920x1080.json`).
- `ScreenshotRecognitionService` распознаёт кадр по слотам раскладки и сохраняет отладочное изображение с рамками слотов.
- `HeroNameReader` читает строку с именем героя встроенным OCR Windows, `HeroNameMatcher` нечётко сопоставляет её со списком героев.
- `DraftStateTracker` превращает последовательность кадров в события выбора (игрок → герой/способность).
- `DraftReportFormatter` формирует текстовый отчёт по игрокам.
