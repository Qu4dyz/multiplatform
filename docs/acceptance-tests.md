# Приймальні тест-кейси GameAnalytics (C1)

Середовище: Windows 11, .NET 9 SDK, гілка `main`. Для кейсів з Riot API потрібен ключ у налаштуваннях, решта працює офлайн.

Колонку "Результат" заповнити перед захистом (пройдено / не пройдено, дата).

| № | Назва | Передумови | Кроки | Очікуваний результат | Автотест | Результат |
|---|---|---|---|---|---|---|
| TC-01 | Запуск з нуля | Чистий клон, немає `game_analytics.db` | `dotnet run --project src/GameAnalytics.Desktop` | Вікно відкривається, база створюється автоматично, без помилок | | |
| TC-02 | Офлайн-режим без ключа | Ключ API порожній | Запустити, відкрити розділи | Застосунок працює на локальних даних, рядок ключа показує, що ключа немає | | |
| TC-03 | Пошук гравця за Riot ID | Валідний ключ | Ввести `Ім'я#TAG`, регіон EUW, Пошук | Профіль, ранг, список матчів | `PlayerAnalyticsViewModel_SelectPlayer_ParsesNameAndTag` | |
| TC-04 | Історія матчів сторінками | У базі > 1 сторінки матчів | Відкрити історію, прокрутити, "Ще" | Спочатку найновіші, далі наступна сторінка | `MatchHistoryViewModel_LoadsOnlyFirstPageThenMore`, `GetRecentMatchesPageAsync_ReturnsNewestFirstPages` | |
| TC-05 | Деталі матчу | Є хоча б 1 матч | Відкрити матч | 5x5 таблиця, предмети, руни, шкала шкоди, графік золота | `PlayerMatchItemViewModel_GoldCurvePoints_NeverNullForAvaloniaPolyline` | |
| TC-06 | Фільтр за чергою | Матчі різних черг | Обрати Solo/Duo | Перераховуються топ-чемпіони і зведення сесії | `PlayerAnalyticsViewModel_ApplyQueueFilter_RecalculatesTopChampionsAndSessionSummary` | |
| TC-07 | Ремейки не псують статистику | Є ремейк | Подивитися winrate | Ремейк позначений і не входить у winrate | `PlayerAnalyticsViewModel_CalculateSessionSummary_ExcludesRemakesFromWinRate` | |
| TC-08 | Коучинг | ≥ 5 матчів гравця | Відкрити звіт коучингу | Показники за напрямами, поради з реальних даних, без даних позначено "немає даних" | `Analyze_MissingVisionData_MarksPillarAsNoDataInsteadOfZero` | |
| TC-09 | Передбачення перемоги на драфті | Модель `fasttree_model.zip` є | Обрати 10 чемпіонів | Ймовірність перемоги синьої команди 0-100% | `PredictionEngineTests` | |
| TC-10 | Синхронізація бази з VPS | VPS доступний | Налаштування -> Синхронізувати | Нова база підміняє стару, історія показує нові дані | `SyncDatabaseFromVps_DownloadsToTempThenSwaps_AndHistoryReadsNewData` | |
| TC-11 | Очищення бази | Є матчі | Налаштування -> Очистити, підтвердити другим кліком | База порожня, екрани оновлено | `MatchHistoryViewModel_ClearNeedsSecondClick`, `ClearDatabase_RaisesDatabaseChanged_AfterConfirmation` | |
| TC-12 | Тренер на Linux (headless) | Linux, .NET 9 | `dotnet run --project src/GameAnalytics.Desktop -- --vps-train` | Тренер стартує без GUI, збирає матчі, віддає статус по HTTP | `VpsHttpSyncServer_ServesStatusAndModelOverHttp` | |
