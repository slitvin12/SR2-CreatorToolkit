namespace CreatorToolkitAgentDirector;

internal static class Help
{
    internal static readonly string[] Endpoints =
    {
        "GET  /ping", "GET  /help", "GET  /state?ui=1&tree=0&hidden=0", "GET  /ui?label=&kind=&canvas=0", "GET  /ui/canvas?tree=1&max=1500",
        "POST /ui/click?label=|id=|kind=&index=|x=&y=|cid=|path=|name=|text= &nth=0&mode=auto|pointer|force|invoke",
        "POST /tab?index=0..7|name=", "POST /menu?action=open|close|toggle",
        "POST /screenshot?name=screen&super=1&wait=1&png=0&marks=off|imgui|canvas|all&grid=0|1|N&scale=1&legend=1",
        "GET  /screenshot/status", "GET  /screenshot.png?name=&view=0",
        "GET  /spawn/list?filter=&category=&spawnable=0&limit=200", "POST /spawn?item=|index=&count=1&distance=2.5&lateral=0",
        "GET  /testpoints", "GET|POST /testpoint?name=test&set=1|x=&y=&z=&yaw=|delete=1",
        "POST /reset", "POST /teleport?point=|x=&y=&z=&yaw=&force=0&anygroup=0",
        "POST /toolkit?cmd=a;b&wait=1&until=m28|any&status=1",
        "GET  /wait?until=ready|gameplay|loaded|player|menu|nomenu|tab|screenshot|focus|noerrors|ui|noui|stable|near&label=&quiet=10&point=&radius=2&frames=&seconds=&timeout=",
        "GET  /assert?noerrors=1&maxerrors=&ready=1&menu=open|closed&tab=&ui=a|b&noui=&kind=&canvas=0&scene=&status=&near=&radius=2&fps=",
        "POST /batch?file=|cmds=a;;b|<body: lines>&stop=1&brief=0&delay=0   (aliases /run, /scenario)",
        "any main-thread command: &shot=NAME (+marks/grid/scale/super) -> \"shot\":{...}; \"during\":{errors,warnings,last} when problems were logged",
        "GET  /errors?level=problems&limit=50", "POST /errors/reset", "GET  /log?since=0&level=all&contains=&limit=100"
    };

    internal const string Text = """
# Creator Toolkit Agent Director 1.1 — шпаргалка агента

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
""";
}
