# Creator Toolkit для Slime Rancher 2

Мод для **Slime Rancher 2** на **MelonLoader**: одно меню с инструментами для игры, исследования мира и съёмки. В нём есть атлас карты с капсулами, гордо, дронами и ресурсами, мини-карта и компас, свои метки, радар, помощник для фермы, спавн, управление временем и погодой, открытия и режим съёмки.

> *English:* Creator Toolkit is a MelonLoader mod for Slime Rancher 2 that adds an in-game toolkit menu (map atlas with pods, gordos, drones and resources, minimap and compass, waypoints, radar, farm helper, spawner, time/weather controls, unlocks and a photo mode). The in-game UI is in Russian.

**Автор:** [slitvin12](https://github.com/slitvin12) · **Версия:** 0.6.0 · Неофициальный фанатский мод, не связан с Monomi Park.

## Требования

- Slime Rancher 2 **1.3.0** (Windows).
- [MelonLoader](https://github.com/LavaGang/MelonLoader) **0.7.x** (проверено на 0.7.3).

## Установка

1. Установите MelonLoader в папку игры и один раз запустите игру.
2. Скачайте `CreatorToolkit.dll` со страницы [Releases](https://github.com/slitvin12/SR2-CreatorToolkit/releases/latest).
3. Положите файл в `<папка игры>\Mods\`.
4. Загрузите сохранение и нажмите **Insert** (на ноутбуке может понадобиться **Fn + Insert**). **Esc** закрывает меню.

Настройки и метки хранятся в `<папка игры>\UserData\CreatorToolkit\`. Действия, которые навсегда меняют прогресс (открытия, разблокировки, атлас), требуют отдельного подтверждения. Перед ними лучше сделать копию сохранения.

## Возможности

| Вкладка | Что умеет |
|---|---|
| Игрок | Бессмертие, бесконечная энергия, скорость ×1–5, полёт / NoClip. |
| Спавн | Каталог в порядке Слаймопедии с поиском: слаймы, еда, плорты, материалы. От 1 до 25 объектов перед камерой, удаление созданного модом. |
| Ресурсы | Деньги, заполнение выбранного слота вакпака. |
| Мир | Перемотка и заморозка времени, погода текущей зоны (включая слаймовый дождь), запрет Варров, заморозка слаймов. |
| Открытия | Слаймопедия, улучшения, двери, зоны и карта, стили слаймов и гаджеты. |
| Съёмка | Скрыть HUD и вакпак, до пяти точек камеры с телепортом. |
| Карта | Атлас, Мои метки, Радар, Ферма, Навигация (подробнее ниже). |
| Удобство | Профили «Исследователь» и «Кадр», SOS, ближайшая неоткрытая капсула, автопополнение, компактный HUD. |

### Карта

- **Атлас.** Капсулы (открытые скрываются по данным сейва), гордо, телепорты, ноды карты, исследовательские и фантомные дроны с маршрутами, ресурсы, двери и механизмы, горшочки и спавнеры. Есть поиск, фильтры слоёв, группировка близких точек и телепорт по ЛКМ с проверкой посадки.
- **Мои метки.** До 32 меток на сейв: имя, 6 цветов, маяк и компас. Метки хранятся отдельно от `.sav`.
- **Радар.** Любимая еда выбранного слайма и редкие материалы в радиусе 100 м.
- **Ферма.** Автосбор плортов и пополнение кормушек загонов из вакпака.
- **Навигация.** Мини-карта: размер S/M/L, масштаб 30–300 м, угол экрана, прозрачность, режим «по взгляду» или «север сверху». Компас: дальность 50–1000 м, от 1 до 16 меток, приоритет фантомных дронов. Настройки сохраняются в `UserData\CreatorToolkit\navigation.cfg`.

Мини-карта, компас и автоматизация фермы по умолчанию выключены.

## Сборка из исходников

Нужны Windows, PowerShell 5.1 и .NET 8 SDK (мод собирается под `net6.0`). Скрипты ищут `dotnet` в `_work\dotnet` внутри репозитория, а сборки игры — в `refs\`. Обе папки не входят в репозиторий.

```powershell
# 1. Локальный .NET 8 SDK
New-Item -ItemType Directory _work -Force | Out-Null
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile _work\dotnet-install.ps1
powershell -ExecutionPolicy Bypass -File _work\dotnet-install.ps1 -Channel 8.0 -InstallDir _work\dotnet

# 2. Сборки игры и MelonLoader из своей установки (после первого запуска с MelonLoader)
$game = 'D:\SteamLibrary\steamapps\common\Slime Rancher 2'
New-Item -ItemType Directory refs -Force | Out-Null
Copy-Item "$game\MelonLoader\Il2CppAssemblies\*.dll" refs\
Copy-Item "$game\MelonLoader\net6\MelonLoader.dll", "$game\MelonLoader\net6\0Harmony.dll", "$game\MelonLoader\net6\Il2CppInterop.Common.dll", "$game\MelonLoader\net6\Il2CppInterop.HarmonySupport.dll", "$game\MelonLoader\net6\Il2CppInterop.Runtime.dll" refs\

# 3. Сборка -> _work\creator-stage\CreatorToolkit.dll
powershell -ExecutionPolicy Bypass -File src\creator\build.ps1

# 4. Офлайн-проверки (игра не запускается)
powershell -ExecutionPolicy Bypass -File src\creator\test.ps1

# 5. Установка проверенной DLL (игра должна быть закрыта)
$env:SR2_GAME_DIR = $game
powershell -ExecutionPolicy Bypass -File src\creator\install.ps1
```

`install.ps1` ставит DLL, только если `test.ps1` прошёл именно для неё. Сценарии проверки в запущенной игре описаны в [`tools/ct-test/TESTING.md`](tools/ct-test/TESTING.md).

## Структура

- `src/creator/` — код мода (C#, IL2CPP через Il2CppInterop) и встроенные данные карты.
- `src/creator/tests/` — офлайн-проверки политик и данных.
- `tools/ct-test/` — сценарии проверки в игре.

## Лицензия

Код мода распространяется по лицензии [MIT](LICENSE). Сторонние материалы под неё не подпадают (подробно — [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)):

- Slime Rancher 2, её названия и графика принадлежат Monomi Park. Сборок и файлов игры в репозитории нет, их берут из своей установки.
- `src/creator/FandomAtlas.rgba` — значки интерактивной карты вики [ru.slimerancher.fandom.com](https://ru.slimerancher.fandom.com) (это графика игры).
- `src/creator/FandomMarkers.tsv` — точки и подсказки, частично основанные на материалах Fandom-вики ([CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/)).
