# Якість коду: тести, покриття, CI, статичний аналіз

## CI

Workflow `.github/workflows/build-and-test.yml` запускається на push у `main` і `feature/**`, на PR у `main` і вручну. Матриця: `windows-latest` і `ubuntu-latest`, кроки: restore, build (Release), тести з покриттям, звіт покриття, звіт аналізатора.

## Тести (C3)

- xUnit, 280 тестів, усі проходять на Windows і Ubuntu.
- Покриття рядків **80.1%**, гілок 62% (coverlet + ReportGenerator), прогін: https://github.com/Qu4dyz/multiplatform/actions/runs/42955513931
- За збірками: Core 85.7%, Infrastructure 86.2%, ML 79.9%, Desktop 70.4%.
- `coverage.runsettings` не рахує Avalonia-оболонку: views (скомпільований XAML), `App`, `Program`, `ViewLocator`. Логіка інтерфейсу живе у ViewModel, і вони в покритті.
- Звіт: артефакт `code-coverage-ubuntu-latest` (HTML і cobertura), підсумок у summary прогону.

Як запустити локально:

```bash
dotnet test GameAnalytics.sln --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

## Статичний аналіз (C4)

- .NET analyzers, `AnalysisLevel=latest-recommended` для всіх проєктів (`Directory.Build.props`).
- Результат: 24 017 рядків C#, 87 унікальних попереджень, 3.6 на 1000 рядків. Прогін: https://github.com/Qu4dyz/multiplatform/actions/runs/42955513931
- Попереджень категорій надійності (Reliability) і безпеки (Security): **0**, тобто critical/major на 1000 рядків: 0.

| Правило | Кількість | Категорія | Суть |
|---|---|---|---|
| CA1305 | 25 | Globalization | форматування рядків без явної культури |
| CA1852 | 16 | Performance | тип можна зробити sealed |
| CA1822 | 14 | Performance | метод можна зробити static |
| CA1861 | 13 | Performance | константний масив як аргумент |
| CA1859 | 10 | Performance | конкретний тип замість інтерфейсу для локальної змінної |
| CA1310 | 6 | Globalization | `StartsWith` без `StringComparison` у тестах |
| CA1847 | 3 | Performance | `Contains(char)` замість `Contains(string)` |

Виправлено під час налаштування (PR #17): культурозалежний `ToLower` в іменах файлів моделей, `IDisposable` для HTTP-сервера і автооновлення, кешовані `JsonSerializerOptions`, перевірка `TryParse`, конкретний тип винятку, передача `CancellationToken`. Задокументовані придушення: CA1001 (`SemaphoreSlim` у синглтонах), CA1310 (`StartsWith`, який EF Core перекладає в SQL `LIKE`), CA1707 (імена xUnit-тестів з підкресленнями).
