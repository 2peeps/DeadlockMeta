# Сервер прицелов

Маленький сервер на Cloudflare Workers. Он считает, сколько раз каждый прицел поставили и лайкнули (из этого
получается сортировка «популярные»), и хранит прицелы, которыми игроки поделились из программы (вкладка
«От игроков»). Бесплатного тарифа Cloudflare хватает с большим запасом: 100 000 запросов в день к Worker и
100 000 записей в день в базу D1.

Сервер хранит только случайный id установки (`Settings.InstallId`), id прицела и время. Ни имён, ни Steam ID, ни IP
в базе нет.

Пока в `Core/CrosshairLibrary.cs` пустой `CrosshairServer.Url`, программа к серверу не обращается: каталог работает,
а счётчиков и вкладки «От игроков» нет.

## Как запустить (через сайт Cloudflare, без установки программ)

1. Зарегистрируйся на [dash.cloudflare.com](https://dash.cloudflare.com) (бесплатно, карта не нужна).
2. **Storage & Databases → D1 → Create database**, имя `deadlockmeta`.
3. Открой базу → вкладка **Console**, вставь содержимое `schema.sql` и нажми **Execute**.
4. **Compute (Workers) → Create → Worker**, имя `deadlockmeta-crosshairs` → **Deploy**.
5. **Edit code**: замени весь код содержимым `worker.js` → **Deploy**.
6. В настройках Worker: **Settings → Bindings → Add → D1 database**, имя переменной `DB`, база `deadlockmeta`.
7. Необязательно: **Settings → Variables and Secrets → Add → Secret** с именем `ADMIN_TOKEN` и длинным случайным
   значением. Оно нужно, чтобы скрывать прицелы с плохими названиями.
8. Скопируй адрес Worker (вида `https://deadlockmeta-crosshairs.<имя>.workers.dev`) и впиши его в
   `CrosshairServer.Url` в `Core/CrosshairLibrary.cs`, без `/` в конце. После этого выпускается новая версия.

Проверка: адрес Worker в браузере отвечает `{"service":"deadlockmeta-crosshairs","ok":true}`, а `/v1/stats` отвечает
`{"items":{}}`.

## Запросы

| Запрос | Что делает |
|---|---|
| `GET /v1/stats` | `{"items": {"c:cross-green": [установок, лайков], …}}`, кэш 5 минут |
| `GET /v1/community` | прицелы от игроков, новые сверху (до 500) |
| `POST /v1/event` | `{install, id, kind: "apply" \| "like" \| "unlike"}` |
| `POST /v1/submit` | `{install, name, code}`: поделиться прицелом, до 5 в день с одной установки |
| `POST /v1/hide` | `{token, id, show?}`: скрыть (или вернуть) прицел от игрока |

Скрыть прицел:

```powershell
Invoke-RestMethod -Method Post -Uri https://<адрес>/v1/hide -ContentType application/json -Body '{"token":"<ADMIN_TOKEN>","id":"u:…"}'
```
