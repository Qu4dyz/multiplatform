# 5 крайніх випадків GameAnalytics (C2)

Відповідно до критерію: валідація, порожні дані, винятки, межі, чистий лог.

## 1. Валідація: неправильний Riot ID і ключ API

- **Вхід.** `Qu4dyz` без `#TAG`, зайві пробіли, ключ не у форматі `RGAPI-...`.
- **Очікувано.** Ім'я і тег розбираються з обрізанням пробілів; ключ невалідного формату вважається відсутнім і не надсилається в API. (Перевірити вручну, що показується без тегу, і за потреби додати підказку.)
- **Доказ.** `PlayerAnalyticsViewModel_SelectPlayer_ParsesNameAndTag`, перевірка ключа в `RiotApiOptions` (тести в `DataLayerTests.cs`).

## 2. Порожні дані: новий гравець або порожня база

- **Вхід.** База щойно створена, у гравця 0 матчів, у матчу немає даних про вижн або команди.
- **Очікувано.** Екрани показують "немає даних", а не 0 і не виняток; коучинг вимагає мінімум 5 матчів.
- **Доказ.** `Analyze_MissingVisionData_MarksPillarAsNoDataInsteadOfZero`, `Analyze_MissingTeamStats_MarksObjectivesAsNoData`, `RetrainModelOnSavedMatches_DefaultIsCappedAndStillNeedsFiveMatches`.

## 3. Винятки зовнішніх систем: 429 від Riot і зіпсований файл з VPS

- **Вхід.** Riot повертає 429 з довгим `Retry-After`; VPS віддає HTML-сторінку помилки замість SQLite.
- **Очікувано.** Без довгого зависання: зрозумілий статус і локальні дані. Локальна база не перезаписується, якщо заголовок не SQLite.
- **Доказ.** `RiotApiClient_GetSummoner_ThrowsOn429WithoutLongSleep`, `RiotRateLimiter_FailFast_WhenBlockExceedsMaxWait`, `SyncDatabaseFromVps_KeepsLocalDatabase_WhenPayloadIsNotSqlite`, `HasSqliteHeader_RejectsNonSqlitePayloads`.

## 4. Межі: дублікати, ремейки, розмір вибірки

- **Вхід.** Той самий `MatchId` зберігається вдруге; матч коротший за 5 хвилин (ремейк); перенавчання на базі з сотнями тисяч матчів.
- **Очікувано.** Дублікат не створюється (унікальний індекс + перевірка); ремейк не рахується у winrate; для навчання береться лише найновіший зріз з обмеженою кількістю рядків.
- **Доказ.** `SaveMatchAsync_DuplicateMatchId_DoesNotThrowOrDuplicate`, `PlayerMatchItemViewModel_DetectsRemake_ByFlagAndDuration`, `RetrainModelOnSavedMatches_UsesOnlyNewestSlice`.

## 5. Чистий лог: падіння UI і аварійне завершення

- **Вхід.** `null` у даних графіка; попередній запуск завершився аварійно і лишив `-wal`/`-shm`.
- **Очікувано.** Графік рендериться з порожньою кривою; необроблений виняток пишеться в `crash.log`, а не губиться; при наступному запуску старі sidecar-файли видаляються, база відкривається.
- **Доказ.** `PlayerMatchItemViewModel_GoldCurvePoints_NeverNullForAvaloniaPolyline`, `ReplaceAsync_DeletesStaleSidecarsLeftByACrashedSession`.
- **На захисті.** Показати, що при звичайному сценарії TC-01...TC-11 у консолі і `crash.log` немає помилок.
