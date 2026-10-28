# Agent Director — шпаргалка AI-агента для живой игры

`CreatorToolkitAgentDirector.dll` (MelonLoader, v1.1.0) — мод-компаньон для Slime Rancher 2 и Creator Toolkit.
Он поднимает локальный мост `http://127.0.0.1:9898`. Через него агент видит состояние игры и UI, кликает,
переключает вкладки, делает скриншоты с разметкой, ждёт условий, проверяет результат и запускает сценарии.
Из корня репозитория всё делается через **`ct`** (`ct.cmd` → `tools\ct.ps1`). Сырые HTTP-эндпоинты описаны в конце.

## Быстрый старт

```
ct status                     # мод установлен? игра запущена? порт, opt-in, ответ моста
ct launch                     # запустить SR2 через Steam и дождаться моста (до 300 с)
ct wait ready timeout=180     # Creator Toolkit готов, мир загружен
ct errors reset               # отсюда считаем только новые ошибки
ct smoke                      # смоук: меню, вкладки 0–7 со снимками, без новых ошибок → exit 0 или 2
ct shot agent marks small     # скриншот + размеченная копия agent.view.png (номера = что кликать)
```

- Коды выхода `ct`: **0** — ок, **1** — ошибка вызова, **2** — мост ответил ошибкой или проверка не прошла (417), **3** — мост недоступен.
- Ответ — компактный JSON в stdout (UTF-8). Любые `k=v` после команды уходят параметрами запроса, кириллица кодируется сама.
- В cmd.exe символ `|` внутри значения берите в кавычки: `ct assert "ui=Спавн|Ресурсы"`.

## Цикл «правка → проверка»

1. Перед копированием DLL в `Mods` закройте игру: `ct kill`. Windows держит загруженные DLL.
2. `ct launch`, затем `ct wait ready timeout=180`.
3. `ct errors reset` — новая база для ошибок.
4. Действия: `ct menu open`, `ct tab Спавн`, `ct click "Розовый слайм"`, …
5. Проверка: `ct assert noerrors menu=open tab=1 "ui=Спавн"`.
6. Визуально: `ct shot check marks small` и откройте файл из поля `view` (PNG с рамками и номерами).
7. Регресс: `ct smoke` или свой сценарий `ct run <имя>`.

## Команды ct

```
ct status                          локально: папка игры, мод (размер, дата), процесс, порт, opt-in, /ping
ct launch [сек] | ct kill          запуск через Steam и ожидание моста | завершить SlimeRancher2.exe (несохранённое пропадёт)
ct ping | ct state [ui=0] [tree=1] живость и FPS | полное состояние: сцена, игрок, тулкит, ошибки, гейты, UI
ct ui [метка] [kind=tab] [canvas=1] видимые IMGUI-элементы тулкита: id, kind, label, x, y, w, h
ct canvas [tree=1]                 нативные uGUI-кнопки игры; их id = cid для клика
ct click <метка|12|c3|960,540>     метка IMGUI (если не нашлась — текст нативной кнопки), IMGUI id, cid, пиксели экрана
ct menu [open|close|toggle]        меню Creator Toolkit (без гейтов)
ct tab <0..7|имя>                  0 Игрок, 1 Спавн, 2 Ресурсы, 3 Мир, 4 Открытия, 5 Съёмка, 6 Карта, 7 Удобство
ct shot [имя] [marks|imgui|canvas] [grid] [small]   скриншот; с флагами ещё <имя>.view.png и легенда
ct png [имя] [файл.png] [view]     скачать готовый снимок (view — размеченный)
ct wait <условие> [арг]            ready | gameplay | loaded | player | menu | nomenu | tab N | ui МЕТКА | noui МЕТКА
                                   | stable [кадров] | near [точка] | screenshot | focus | noerrors
ct assert <проверки>               noerrors maxerrors=N ready menu=open|closed tab=N|имя ui=A|B noui=X kind= canvas=1
                                   scene= status= near=точка radius=2 fps=30
ct errors [reset] | ct log [текст] ошибки с последнего сброса | лента сообщений MelonLoader и Unity
ct smoke | ct run <сценарий>       сценарий из <SR2>\UserData\CreatorToolkitAgent\scenarios\ (brief=1)
ct batch <файл.txt|"строка;;строка"> свои строки через /batch (stop=0, delay=мс)
ct tk "cmd;cmd" [until=m28]        ретрансляция в DiagnosticIo Creator Toolkit
ct spawnlist [фильтр] | ct spawn <предмет> [кол-во]          тестовый спавн (opt-in + тестовый сейв)
ct point [имя] | ct points | ct reset | ct tp <точка|x,y,z>  тестовые точки и телепорт (opt-in + тестовый сейв)
ct optin on|off|status             файл-разрешение UserData\CreatorToolkit\ct-test.enabled
ct api                             полная HTTP-шпаргалка из самого мода (/help)
ct get|post </путь?query> [тело]   сырой запрос;  ct ipc "строка" — принудительно через файловый IPC
```

## Добавки к любой игровой команде

- `shot=ИМЯ` — снимок сразу после действия, в ответе `"shot":{path, view, legend…}`. Работает вместе с `marks=imgui|canvas|all`, `grid=1`, `scale=0.5`, `super=2`.
  Пример: `ct tab 2 shot=tab2 marks=imgui scale=0.5`.
- `"during":{errors, warnings, last[]}` появляется в ответе сам, если во время команды в лог попали ошибки или предупреждения.
- `timeout=N` — свой тайм-аут в секундах (ct ждёт на 20 с дольше).

## Скриншоты с разметкой

`ct shot NAME marks small` создаёт в `<SR2>\UserData\CreatorToolkitAgent\screenshots\`:
`NAME.png` (оригинал), `NAME.view.png` (рамки и номера, уменьшено вдвое) и `NAME.view.json` (легенда).

- Номер `12` на рамке — IMGUI-элемент тулкита: `ct click 12`. Номер `c3` — нативная кнопка игры: `ct click c3`.
- `grid` — сетка через 100 px с подписями экранных координат: `ct click 960,540`.
- Легенда: `tag, kind, label, x, y, w, h` (пиксели экрана) и `click` (`id=12` или `cid=3`). Цвет рамки зависит от kind.
- IMGUI id действителен для текущего экрана. cid стабилен, пока экран не меняется (порядок чтения: сверху вниз, слева направо).
- Окно игры не должно быть свёрнуто: без отрисовки кадров снимок уходит в тайм-аут.

## Сценарии

Файл — по команде в строке: `[GET|POST] /путь?параметры [тело]`. `#` — комментарий, `;;` — разделитель,
пробелы в значениях — `%20` или `+`.

```
# my-check.txt
GET  /wait?until=ready&timeout=180
POST /errors/reset
POST /menu?action=open
POST /tab?index=1&shot=spawn&marks=imgui&scale=0.5
GET  /wait?until=stable&quiet=10
GET  /assert?noerrors=1&tab=1
```

- `ct batch my-check.txt` — локальный файл. Или положите его в `<SR2>\UserData\CreatorToolkitAgent\scenarios\` и запускайте `ct run my-check`.
- Ответ: `steps, ran, passed, failedAt, failedLine, results[{i, line, status, ms, body}]`; HTTP 200 или 417 (ct → exit 2).
- По умолчанию стоп на первой ошибке (`stop=0` — до конца). `brief=1` — тела только у упавших шагов. До 300 шагов.
- `smoke.txt` мод создаёт сам при первом запуске; правьте копию под своим именем.

## Гейты и безопасность

- Только loopback `127.0.0.1`. Браузерные запросы (заголовки Origin, Sec-Fetch-*) отклоняются.
- Клики, спавн и телепорт работают только после `ct optin on` (файл `UserData\CreatorToolkit\ct-test.enabled`).
- Спавн и телепорт требуют ещё и загруженный тестовый сейв Creator Toolkit (`ct state` → `gates`).
- Меню, вкладки, скриншоты и наблюдение работают без гейтов.
- Настройки: `UserData\MelonPreferences.cfg` → `[CreatorToolkitAgent]`: Port, HttpEnabled, FileIpcEnabled, RequireOptIn, RequireTestSave.

## Если что-то не так

- exit 3, unreachable — игра не запущена (`ct launch`), ещё грузится или мод не установлен (`ct status`).
- 403 — нет opt-in или загружен не тестовый сейв: смотрите `gates` в `ct state`.
- 409 — навигация отклонена (открыто подтверждение, фокус в нативном UI, переход): `toolkit.lastUiNavigation`, `toolkit.pending`.
- 503 — смена сцены или тулкит не готов: `ct wait ready`.
- 504 — тайм-аут; у скриншота — обычно свёрнутое окно.
- 417 — проверка не прошла: `failures[]` в ответе, `recentErrors` с текстом ошибок.
- HTTP выключен — ct сам переходит на файловый IPC (`ipc\cmd.txt` → `ipc\out.jsonl`), если игра запущена.

## Файлы и сборка

- Мод: `<SR2>\Mods\CreatorToolkitAgentDirector.dll`, где `<SR2>` = `D:\SteamLibrary\steamapps\common\Slime Rancher 2` (или `SR2_DIR`).
- Данные: `<SR2>\UserData\CreatorToolkitAgent\` — `bridge.json` (фактический порт), `AGENT-COMMANDS.md` (то же, что `ct api`),
  `screenshots\`, `scenarios\` (`smoke.txt`), `ipc\`, `testpoints.txt`.
- Исходники: `src\agentdirector`. Сборка: `_work\agentbridge\build-agent.cmd`, то есть
  `_work\dotnet\dotnet.exe build src\agentdirector\CreatorToolkitAgentDirector.csproj -c Release -o _work\agentdirector-stage --no-incremental`
  (требование: 0 предупреждений, 0 ошибок). Установка: `ct kill`, затем скопировать `_work\agentdirector-stage\CreatorToolkitAgentDirector.dll`
  в `<SR2>\Mods\` и сверить SHA-256.

## HTTP напрямую (то же отдаёт `ct api`)

```text
Мост: http://127.0.0.1:9898 (только loopback). Если порт занят — 9899…9905; фактический порт в
UserData\CreatorToolkitAgent\bridge.json. GET и POST равнозначны; параметры — query, form или JSON-тело.
Из репозитория удобнее обёртка ct.cmd (ct help) — см. AGENT-DIRECTOR.md в корне репозитория.
Ответы — JSON {"ok":..,"id":..,"path":..,"frame":..}. Коды: 200 OK, 202 принято (асинхронно),
400 параметры, 403 гейт (opt-in / тестовый сейв), 404 не найдено, 409 состояние игры не позволяет,
417 проверка не прошла (/assert, /batch), 429 очередь тулкита полна, 501 в Creator Toolkit нет нужного
члена, 503 тулкит/мир недоступен или смена сцены, 504 тайм-аут. В любой команде: timeout=<секунды>.
Кириллица: curl -G ... --data-urlencode "label=Спавн" (%-кодирование UTF-8; CP1251 тоже принимается).

## Добавки к любой команде главного потока (state, ui, click, tab, menu, spawn, teleport, wait, toolkit…)
shot=ИМЯ — сразу после команды снять экран; результат в "shot":{...}. Передаются marks=, grid=, scale=, super=.
"during":{errors, warnings, last[]} — появляется сам, если за время команды в лог попали ошибки/предупреждения.
  curl -s -X POST "http://127.0.0.1:9898/tab?index=2&shot=tab2&marks=imgui&scale=0.5"

## Наблюдение
curl -s http://127.0.0.1:9898/ping                     # живость, FPS, сцена, ready/menuOpen (без главного потока)
curl -s http://127.0.0.1:9898/state                    # fps, screen, scene, zone, player, camera, toolkit, errors, screenshot, gates, ui
curl -s "http://127.0.0.1:9898/state?tree=1&max=800"   # + дерево активного uGUI (Canvas → GameObject → компоненты, текст, rect)
curl -s "http://127.0.0.1:9898/state?ui=0"             # без UI (дешевле)
curl -s http://127.0.0.1:9898/ui                       # видимые IMGUI-элементы тулкита: id, kind, label, index, on, x,y,w,h, cx,cy
curl -s "http://127.0.0.1:9898/ui?canvas=1"            # + нативные uGUI-кнопки/тумблеры игры (их id = cid)
curl -s -G http://127.0.0.1:9898/ui --data-urlencode "label=полёт"   # фильтр по подстроке
curl -s "http://127.0.0.1:9898/ui/canvas?tree=1"       # selectables + полное дерево uGUI
curl -s http://127.0.0.1:9898/errors                   # счётчики MelonLoader / Unity / Latest.log + последние ошибки
curl -s -X POST http://127.0.0.1:9898/errors/reset     # новая база для errors (считаем только новые)
curl -s "http://127.0.0.1:9898/log?since=0&level=all&limit=100"   # лента сообщений Melon/Unity (level=error|warning|msg|unity)

## Скриншоты и разметка (ScreenCapture.CaptureScreenshot)
curl -s -X POST "http://127.0.0.1:9898/screenshot?name=agent"                       # ждёт файла: state=ready, path, width, height
curl -s -X POST "http://127.0.0.1:9898/screenshot?name=agent&marks=all&scale=0.5"   # + agent.view.png с рамками и номерами
curl -s -X POST "http://127.0.0.1:9898/screenshot?name=agent&grid=1"                # + сетка с экранными координатами
curl -s -o shot.png "http://127.0.0.1:9898/screenshot?png=1"                        # сразу байты PNG (с marks=/grid= — размеченные)
curl -s -o view.png "http://127.0.0.1:9898/screenshot.png?name=agent&view=1"        # готовый файл (view=1 — размеченный)
curl -s -X POST "http://127.0.0.1:9898/screenshot?wait=0"  → 202; затем /screenshot/status (idle | pending | ready | failed)
marks = imgui (элементы Creator Toolkit, номер = id для click id=) | canvas (кнопки игры, номер cN = click cid=N)
      | all | off. grid=1 — линии каждые 100 px экрана (grid=N — шаг N от 20), подписи — координаты для click x=,y=.
scale=0.1..1 — уменьшенная размеченная копия (дешевле смотреть). super=2..4 — суперсэмплинг исходника.
Файлы в UserData\CreatorToolkitAgent\screenshots: <name>.png (исходник), <name>.view.png (разметка),
<name>.view.json (легенда: tag, kind, label, x,y,w,h в пикселях экрана, click). Легенда есть и в ответе (legend=0 — без неё).
Поля ответа: state, path, width, height, view, viewWidth, viewHeight, marks (нарисовано), viewError.
Готовность = файл стабилен, закрыт Unity и содержит IHDR+IEND. Свёрнутое окно не рисует кадры → тайм-аут.

## Меню и вкладки Creator Toolkit (без гейтов, только UI)
curl -s -X POST "http://127.0.0.1:9898/menu?action=open"       # close | toggle
curl -s -X POST "http://127.0.0.1:9898/tab?index=6"            # 0 Игрок, 1 Спавн, 2 Ресурсы, 3 Мир, 4 Открытия, 5 Съёмка, 6 Карта, 7 Удобство
curl -s -X POST -G http://127.0.0.1:9898/tab --data-urlencode "name=Карта"
Ответ: toolkit{menuOpen, tab, tabName, status, lastError, pending, lastUiNavigation}; 409 — навигация отклонена
(причина в lastUiNavigation: not-ready / native-focus / transit / confirmation-active).

## Клики по UI (нужен opt-in)
curl -s -X POST -G http://127.0.0.1:9898/ui/click --data-urlencode "label=Спавн"
curl -s -X POST "http://127.0.0.1:9898/ui/click?id=12"                  # IMGUI id из /ui или метка на снимке marks=imgui
curl -s -X POST "http://127.0.0.1:9898/ui/click?cid=3"                  # нативная кнопка: id из /ui/canvas или метка c3 на снимке
curl -s -X POST "http://127.0.0.1:9898/ui/click?kind=tab&index=2"
curl -s -X POST -G http://127.0.0.1:9898/ui/click --data-urlencode "label=Бесконечные" -d kind=toggle -d nth=0
curl -s -X POST "http://127.0.0.1:9898/ui/click?x=960&y=540"            # мышь в пиксели экрана (0,0 = левый верхний угол)
curl -s -X POST -G http://127.0.0.1:9898/ui/click --data-urlencode "text=Продолжить"   # нативный uGUI (или path= / name=)
label: сначала точное совпадение (без регистра, ё=е), затем подстрока. kind: button|tab|icon|toggle|category|role.
cid стабилен, пока экран не меняется: порядок чтения (сверху вниз, слева направо), затем путь в иерархии.
mode=auto (по умолчанию): синтетическая мышь InputSystem; если тулкит не засчитал нажатие — force.
mode=pointer — только мышь; mode=force — нажатие засчитывается прямо в отрисовке элемента (hidden=1 — и вне
области прокрутки); mode=invoke — uGUI onClick/isOn/value. Ответ: registered, via, toolkit{...}.
Если toolkit.pending ≠ null — открыто подтверждение: найдите его кнопку в /ui и кликните.

## Ожидание
curl -s "http://127.0.0.1:9898/wait?until=ready&timeout=180"
curl -s -G http://127.0.0.1:9898/wait -d until=ui --data-urlencode "label=Спавн"   # элемент появился (noui — исчез)
curl -s "http://127.0.0.1:9898/wait?until=stable&quiet=15"                         # IMGUI не меняется 15 кадров подряд
curl -s "http://127.0.0.1:9898/wait?until=near&point=test&radius=2"                # игрок у тестовой точки
until = ready | gameplay | loaded | player | menu | nomenu | tab (+tab=N) | screenshot | focus | noerrors
      | ui (+label=, kind=, canvas=1) | noui (+label=) | stable (+quiet=10) | near (+point=test, radius=2);
frames=N; seconds=S

## Проверки: /assert (200 — всё прошло, 417 — failures[{check, expected, actual}] + recentErrors)
curl -s "http://127.0.0.1:9898/assert?noerrors=1&menu=open&tab=2"
curl -s -G http://127.0.0.1:9898/assert --data-urlencode "ui=Спавн|Ресурсы" -d kind=tab
noerrors=1 (ошибок с последнего /errors/reset нет) | maxerrors=N | ready=1 | menu=open|closed | tab=N|имя
| ui=метка[|метка2] | noui=метка | kind= | canvas=1 (искать и в uGUI) | scene=подстрока | status=подстрока статуса
тулкита | near=точка&radius=2 | fps=30 (минимальный средний FPS). Ответ: passed, failed, pass[], failures[], toolkit.

## Сценарии: /batch (синонимы /run, /scenario)
Строки выполняются по очереди через тот же конвейер (у каждой свои shot= и during). Формат строки:
[GET|POST] /путь?параметры [тело]; пробелы в значениях — %20 или +; # — комментарий; ;; — разделитель строк.
curl -s -X POST "http://127.0.0.1:9898/batch?file=smoke&brief=1"     # UserData\CreatorToolkitAgent\scenarios\smoke.txt
curl -s -X POST -H "Content-Type: text/plain" --data-binary @steps.txt http://127.0.0.1:9898/batch
curl -s -X POST -G http://127.0.0.1:9898/batch --data-urlencode "cmds=POST /menu?action=open;;GET /assert?menu=open"
stop=1 (по умолчанию стоп на первой ошибке; stop=0 — до конца), brief=1 (тела только у упавших шагов), delay=мс.
Ответ: steps, ran, passed, failedAt, failedLine, ms, results[{i, line, status, ms, body}]; 200 или 417. До 300 шагов,
вложенный /batch запрещён. smoke.txt создаётся при первом запуске: ready → errors/reset → меню → вкладки 0–7
со снимками smoke-tabN (marks=imgui, scale=0.5) → assert noerrors → закрыть меню → assert.

## Тестовый спавн (opt-in + тестовый сейв)
curl -s -G http://127.0.0.1:9898/spawn/list --data-urlencode "filter=розов" -d spawnable=1 -d limit=20
curl -s -X POST -G http://127.0.0.1:9898/spawn --data-urlencode "item=Розовый слайм" -d count=2 -d distance=3
curl -s -X POST "http://127.0.0.1:9898/spawn?index=15&count=1"         # index из /spawn/list; item=#15 тоже работает
Создание идёт через ActorTools.Spawn тулкита (те же проверки, что и в меню). count ≤ 50.

## Тестовые точки и сброс игрока (opt-in + тестовый сейв)
curl -s -X POST "http://127.0.0.1:9898/testpoint?name=test&set=1"      # запомнить текущую позицию и взгляд
curl -s "http://127.0.0.1:9898/testpoint?name=base&x=10&y=20&z=30&yaw=90"
curl -s http://127.0.0.1:9898/testpoints
curl -s -X POST http://127.0.0.1:9898/reset                             # в точку test (если нет — авто-точка start на момент загрузки мира)
curl -s -X POST "http://127.0.0.1:9898/teleport?point=base"
curl -s -X POST "http://127.0.0.1:9898/teleport?x=10&y=20&z=30&force=1" # force=1 — без проверки свободной капсулы
По умолчанию PlayerTools.PlaceForMap тулкита; ответ: from, final, distance, arrived. Точки: UserData\CreatorToolkitAgent\testpoints.txt.

## Команды Creator Toolkit (ретрансляция в его DiagnosticIo — как cmd.txt, но без задержки 0.5 с)
curl -s -X POST "http://127.0.0.1:9898/toolkit?cmd=status&status=1"       # statusTxt = UserData\CreatorToolkit\status.txt
curl -s -X POST "http://127.0.0.1:9898/toolkit?cmd=menu-player;catalog&wait=2"
curl -s -X POST "http://127.0.0.1:9898/toolkit?cmd=ct-state&until=m28&timeout=60"   # ждать изменения M28TestIpc.Last
until = m26 | m27 | m28 | status | lastError | uiNavigation | any. Ответ: last{...}, changed[], log[] (сообщения за
время команды), files[] (изменённые файлы UserData\CreatorToolkit). ct-/m28-команды тулкит сам проверяет по ct-test.enabled.

## Файловый IPC (если HTTP недоступен)
Запишите UserData\CreatorToolkitAgent\ipc\cmd.txt (UTF-8, по команде в строке), например:
  GET /state?ui=0
  POST /ui/click {"label":"Спавн"}
  /screenshot?name=agent
Результаты: ipc\out.jsonl (строка на команду: id, line, status, body) и ipc\last.json (последний ответ).
Файл забирается атомарно (переименованием) каждые 100 мс; пишите его целиком (через временный файл + rename).

## Гейты безопасности
Клики, спавн и телепорт требуют opt-in файл UserData\CreatorToolkit\ct-test.enabled (пустой; ct optin on).
Спавн и телепорт — ещё и загруженный тестовый сейв Creator Toolkit (см. /state → gates).
Отключение: UserData\MelonPreferences.cfg → [CreatorToolkitAgent] RequireOptIn / RequireTestSave = false.
Там же: Port, HttpEnabled, FileIpcEnabled. Запросы с заголовками Origin/Sec-Fetch-* (браузер) отклоняются.

## PowerShell
Invoke-RestMethod http://127.0.0.1:9898/state | ConvertTo-Json -Depth 10
Invoke-RestMethod -Method Post ("http://127.0.0.1:9898/ui/click?label=" + [uri]::EscapeDataString("Спавн"))
```
