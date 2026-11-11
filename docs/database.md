# Звіт C5: база даних GameAnalytics

СУБД: SQLite через EF Core 9 (`Microsoft.EntityFrameworkCore.Sqlite`). Контекст: `src/GameAnalytics.Infrastructure/Persistence/GameAnalyticsDbContext.cs`, доступ до даних: `Repositories/MatchRepository.cs`.

## Схема

| Таблиця | PK | FK | Індекси |
|---|---|---|---|
| `Matches` | `Id` | | `MatchId` UNIQUE; `GameCreation` |
| `Participants` | `Id` | `MatchEntityId -> Matches.Id`, ON DELETE CASCADE | індекс на FK (EF створює автоматично); `Puuid` |
| `TeamStats` | `Id` | `MatchEntityId -> Matches.Id`, ON DELETE CASCADE | індекс на FK |
| `PlayerLpSnapshots` | `Id` | | (`Puuid`, `QueueId`) |
| `PlayerMatchLpRecords` | `Id` | | (`Puuid`, `MatchId`) |
| `MatchTimelineCaches` | `Id` | | `MatchId` UNIQUE |
| `PlayerRankHints` | `Id` | | `Puuid` UNIQUE |

Зв'язки: `Match 1 - N Participant`, `Match 1 - N TeamStats` (`HasMany(...).WithOne().HasForeignKey(...).OnDelete(Cascade)`).

## 1. Нормалізація (3НФ)

- Кожна таблиця має сурогатний PK, атрибути залежать лише від нього: дані матчу в `Matches`, дані учасника в `Participants`, дані команди в `TeamStats`. Транзитивних залежностей між неключовими атрибутами немає, тобто основна схема в 3НФ.
- **Свідомі відхилення:**
  - `Participants.Item0...Item6` є повторюваною групою (порушення 1НФ). Залишено, бо слотів завжди рівно 7, порядок важливий і їх завжди читають разом; окрема таблиця дала б +7 рядків на учасника без користі.
  - `Participants.ChallengesJson` зберігає сирий JSON від Riot для перерахунку ознак ML; критичні поля з нього винесені в окремі колонки.
  - `SummonerName` і `Puuid` повторюються в кожному матчі (немає таблиці `Players`). Це знімок імені на момент гри, Riot ID може змінюватися.

## 2. PK/FK і цілісність

PK у всіх таблицях, FK з каскадним видаленням для дочірніх таблиць матчу, унікальність `MatchId` захищає від дублікатів (тест `SaveMatchAsync_DuplicateMatchId_DoesNotThrowOrDuplicate`).

## 3. Індекси

Унікальні індекси на природних ключах (`MatchId`, `Puuid`), складені індекси під запити трекінгу LP, індекс `GameCreation` для вибірки N найновіших матчів (див. лог рішень, запис 6).

Запити історії гравця фільтрують `Participants.Puuid` (`Where(m => m.Participants.Any(p => p.Puuid == puuid))`), для цього є індекс `IX_Participants_Puuid`: `HasIndex(p => p.Puuid)` для нових баз і `CREATE INDEX IF NOT EXISTS` для існуючих (PR #14).

## 4. Параметризовані запити

- Усі запити з даними користувача йдуть через LINQ, EF Core передає значення як параметри (`@__puuid_0`), SQL-ін'єкція неможлива.
- `ExecuteSqlRaw` використовується лише для DDL і службових `UPDATE` з константним текстом, без конкатенації вхідних даних.
- Правило для нових запитів: тільки `ExecuteSqlInterpolated` / `FromSqlInterpolated` або параметри.

## 5. Транзакції

- Кожен `SaveChangesAsync` в EF Core виконується в одній транзакції: матч разом з учасниками і командами записується атомарно.
- Заміна файлу бази при синхронізації з VPS атомарна на рівні файлової системи (тимчасовий файл + перейменування).
- `UpsertMatchAsync` (заміна вже збереженого матчу) видаляє старий запис і додає новий в одній явній транзакції, тож збій вставки не губить матч (PR #14):

```csharp
var ownsTransaction = _context.Database.CurrentTransaction == null;
await using var tx = ownsTransaction ? await _context.Database.BeginTransactionAsync(ct) : null;
// Remove(existing) + SaveChanges, Add(match) + SaveChanges
if (tx != null) await tx.CommitAsync(ct);
```

- Очищення кешу таймлайнів - параметризований `ExecuteSqlInterpolatedAsync` з `LIMIT`/`OFFSET` (PR #12).

## Висновок

Виконано всі п'ять пунктів: схема в 3НФ з обґрунтованими відхиленнями, PK/FK з каскадом, індекси під реальні запити, параметризовані запити, транзакції (неявні в `SaveChanges` і явна в `UpsertMatchAsync`).
