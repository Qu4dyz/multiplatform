# Chat Handoff — GameAnalytics (2026-09-19)

Короткий стан для нового чату. Не підвантажуй старий transcript.

## Стек
.NET 9 + Avalonia 11 + ML.NET FastTree + EF Core SQLite. Repo: `Qu4dyz/multiplatform`, branch `main`.

## Готово
- Desktop: sidebar UI, match cards (spells/runes/items), mastery row, coaching pillars, LP snapshots, cache warm-up, `appsettings.local.json` (gitignored).
- VPS trainer: `gameanalytics-trainer.service` @ `45.77.53.46:5050` (`/api/status`, `/api/model`, `/api/database`).
- Git: AI co-author stripped via `.git/hooks/commit-msg`. Не згадувати Cursor/AI у комітах.

## ML зараз (після фіксу краулера `7704bc7`)
- Баг: при скіпі відомих матчів не розширювався граф гравців → 0 нових матчів.
- Фікс: expand з participants, random DB seeds, deeper lookback 30, Challenger/GM/Master + summonerId→puuid, cached match count для status.
- Live після деплою: **971** матчів (було 951), Acc **97.3%**, AUC **0.993**, F1 **0.970**, епоха крутиться кожні 2 хв.

## Ключові шляхи
- UI: `src/GameAnalytics.Desktop/Views/PlayerAnalyticsView.axaml`
- Crawler: `src/GameAnalytics.ML/Training/RealMatchDatasetCollector.cs`
- Ladder: `RiotApiClient.GetChallengerPlayerPuuidsAsync`
- Sync: `VpsHttpSyncServer` / `VpsSyncService`
- Roadmap (довгий): `PROJECT_STATUS_AND_ROADMAP.md`

## Далі (пріоритети)
1. Дати краулеру наростити датасет (перевіряти `/api/status`).
2. Періодично sync модель/DB на ПК (Settings або scp).
3. UI polish лише за багами з оверлапами.
4. Розширити фічі ML (більше early-game signals) коли база >2–3k матчів.

## Команди
```powershell
# status
Invoke-RestMethod http://45.77.53.46:5050/api/status

# VPS logs
ssh -i $HOME\.ssh\av_vps_ed25519 root@45.77.53.46 "tail -n 40 /var/log/gameanalytics-trainer.log"

# pull model
scp -i $HOME\.ssh\av_vps_ed25519 root@45.77.53.46:/root/multiplatform/src/GameAnalytics.Desktop/bin/Release/net9.0/models/fasttree_model.zip src/GameAnalytics.Desktop/models/fasttree_model.zip
```

Новий чат: `@CHAT_HANDOFF.md` + конкретна задача. Старий чат можна не продовжувати.
