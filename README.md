# Jellyfin Trailer Plugin

> **Vibe coding:** этот плагин полностью написан с помощью vibe coding в паре с Claude.

Плагин добавляет кнопку **«Трейлер»** на страницу каждого фильма в веб-интерфейсе Jellyfin.
По клику открывается список найденных на YouTube трейлеров; выбранный воспроизводится
встроенным YouTube-плеером прямо в интерфейсе, без ухода на сайт YouTube.

![Jellyfin](https://img.shields.io/badge/Jellyfin-10.10%2B-blue?logo=jellyfin)
![.NET](https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet)
![License](https://img.shields.io/github/license/dobriys/jellyfin_trailer)

---

## Важная особенность: только веб-интерфейс

Плагин рисует кнопку через клиентский JavaScript (`custom.js`), который исполняет
**только веб-клиент Jellyfin** (браузер по адресу `http://<сервер>:8096/web`).

**В нативных приложениях кнопки не будет** — приложения для Android TV, телевизоров,
iOS/Android и прочие клиенты не загружают `custom.js`. Это ограничение архитектуры
Jellyfin, а не плагина. Пользоваться плагином нужно через браузер.

Воспроизведение идёт через встраивание `youtube-nocookie.com`, поэтому **устройство,
на котором открыт веб-интерфейс, должно иметь доступ к YouTube**. Если YouTube в вашей
сети недоступен, встроенный плеер не загрузится (останется ссылка «Открыть на YouTube»).

---

## Как это работает

1. На странице фильма нажимаете **«Трейлер»**.
2. Плагин ищет трейлеры на YouTube через **YouTube Data API v3** по запросу вида
   `«название фильма» трейлер «год»`.
3. Открывается модальное окно со списком результатов: превью, название, канал, дата.
4. Вы выбираете нужный ролик — он воспроизводится встроенным плеером в том же окне.
5. Если владелец ролика запретил встраивание, показывается ссылка **«Открыть на YouTube»**.

Фильм определяется по **названию и году** из метаданных Jellyfin — отдельные ID
(TMDb, Kinopoisk) не требуются.

---

## Требования

- Jellyfin Server **10.10.x** или новее
- Бесплатный ключ **YouTube Data API v3** (см. «Настройка»)
- Доступ к YouTube с устройства, где открыт веб-интерфейс

---

## Установка

### Через репозиторий плагинов

1. Откройте **Панель управления → Плагины → Репозитории**.
2. Нажмите **+** и добавьте адрес:

   ```
   https://raw.githubusercontent.com/dobriys/jellyfin_trailer/main/dist/manifest.json
   ```

3. Перейдите в **Каталог плагинов**, найдите **Trailer** в категории **General**.
4. Нажмите **Установить** и перезапустите Jellyfin.

### Ручная установка

1. Скачайте последний `Jellyfin.Plugin.Trailer_*.zip` со страницы
   [Releases](https://github.com/dobriys/jellyfin_trailer/releases).
2. Создайте папку плагина и распакуйте в неё содержимое `.zip` (`.dll` и `.xml`):
   - **Docker:** `/config/data/plugins/Trailer_<версия>/`
   - **Linux** (apt/rpm): `/var/lib/jellyfin/plugins/Trailer_<версия>/`
   - **Windows:** `%ProgramData%\Jellyfin\Server\plugins\Trailer_<версия>\`
3. Убедитесь, что папка доступна на запись процессу Jellyfin
   (в Docker обычно `chown -R 1000:1000`).
4. Перезапустите Jellyfin.

> Держите ровно одну папку `Trailer_*`. Старые версии удаляйте, иначе сервер может
> путаться при чтении плагина и его настроек.

---

## Настройка

Откройте **Панель управления → Плагины → Trailer**.

| Параметр | Описание |
|---|---|
| **YouTube Data API v3 Key** | Ключ для поиска трейлеров на YouTube. Единственная настройка плагина. |

### Получение ключа YouTube Data API v3

1. Откройте [Google Cloud Console](https://console.cloud.google.com/) под своим аккаунтом.
2. Создайте проект: **Select a project → New Project**.
3. Включите API на странице
   [YouTube Data API v3](https://console.cloud.google.com/apis/library/youtube.googleapis.com)
   → **Enable** (проверьте, что выбран ваш проект).
4. Создайте ключ: **APIs & Services → Credentials → Create Credentials → API key**.
5. Вставьте ключ в поле настроек плагина и нажмите **Сохранить**.

Бесплатный лимит — 10 000 единиц в сутки; один поиск стоит 100 единиц (около 100 поисков в день).

> **Ограничения ключа.** Плагин обращается к API **с сервера**, поэтому в настройках ключа:
> - **Application restrictions** — **None** или **IP addresses** (публичный IP сервера),
>   но **не** «HTTP referrers» (иначе серверные запросы блокируются).
> - **API restrictions** — «Don't restrict» либо обязательно с включённой
>   **YouTube Data API v3**.
>
> Вставляйте ключ так, чтобы в поле не попали лишние символы: полностью очистите поле
> (Ctrl+A → Delete), откажитесь от подсказок автозаполнения браузера и вставьте ровно ключ.

---

## Подключение кнопки к интерфейсу

Кнопка добавляется через JavaScript. Готовые сниппеты для копирования есть прямо в
настройках плагина: **Панель управления → Плагины → Trailer**.

### Способ 1 — файл `custom.js` (рекомендуется)

Jellyfin автоматически загружает `custom.js` из папки веб-интерфейса, если файл существует.

| Установка | Путь к файлу |
|---|---|
| **Docker** (`jellyfin/jellyfin`) | `/jellyfin/jellyfin-web/custom.js` |
| **Docker** (linuxserver) | `/usr/share/jellyfin/web/custom.js` |
| **Linux** (apt/rpm/tar) | `/usr/share/jellyfin/web/custom.js` |
| **Windows** | `C:\Program Files\Jellyfin\Server\jellyfin-web\custom.js` |
| **Synology DSM** | `/volume1/@appstore/Jellyfin/package/jellyfin-web/custom.js` |

Содержимое файла (замените адрес на адрес вашего Jellyfin):

```js
(function(){
  var s = document.createElement('script');
  s.src = 'https://ВАШ_JELLYFIN/web/configurationpage?name=trailerPlugin_js';
  document.head.appendChild(s);
})();
```

> **Docker:** если папка `jellyfin-web` не примонтирована как volume, файл после каждого
> обновления образа затирается. Пробросьте папку через volume либо копируйте файл заново:
> ```bash
> docker cp custom.js jellyfin:/jellyfin/jellyfin-web/custom.js
> ```

После создания файла перезапустите Jellyfin и обновите страницу браузера (Ctrl+Shift+R).

### Способ 2 — Nginx reverse proxy

Если Jellyfin стоит за Nginx, добавьте в блок `location` директиву `sub_filter`:

```nginx
location / {
    proxy_pass http://localhost:8096;

    sub_filter '</head>'
      '<script src="http://localhost:8096/web/configurationpage?name=trailerPlugin_js"></script></head>';
    sub_filter_once on;
    sub_filter_types text/html;
}
```

---

## Использование

После подключения кнопка **«Трейлер»** появляется на странице каждого фильма рядом с
кнопками воспроизведения (в браузере).

- Клик по кнопке открывает список найденных трейлеров.
- Выбор ролика запускает встроенный плеер в том же окне.
- Плеер закрывается кнопкой закрытия, кликом по фону или клавишей `Escape`.
- Если трейлеры не найдены, показывается сообщение «Трейлеры не найдены».

---

## Совместимость

| Компонент | Значение |
|---|---|
| Jellyfin Server | 10.10.x и выше |
| .NET Runtime | 8.0 |
| Клиент | Только веб-интерфейс в браузере (Chrome, Firefox, Safari, Edge) |
| Нативные приложения | Не поддерживаются (см. «Важная особенность») |

---

## Лицензия

[MIT](LICENSE)

---

# English

> **Vibe coding:** this plugin was written entirely using vibe coding with Claude.

The plugin adds a **«Trailer»** button to every movie page in the Jellyfin **web interface**.
Clicking it opens a list of trailers found on YouTube; the selected one plays in an embedded
YouTube player inside the interface, without leaving for YouTube.

---

## Key characteristic: web interface only

The button is injected via client-side JavaScript (`custom.js`), which only the
**Jellyfin web client** (a browser at `http://<server>:8096/web`) executes.

**Native apps will not show the button** — Android TV, TV, iOS/Android, and other clients
do not load `custom.js`. This is a Jellyfin architecture limit, not a plugin bug. Use the
plugin through a browser.

Playback uses a `youtube-nocookie.com` embed, so **the device viewing the web interface
must be able to reach YouTube**. If YouTube is blocked in your network, the embedded player
will not load (an «Open on YouTube» link remains).

---

## How it works

1. On a movie page, click **«Trailer»**.
2. The plugin searches YouTube via the **YouTube Data API v3** for `«movie title» трейлер «year»`.
3. A modal shows the results: thumbnail, title, channel, date.
4. You pick a video — it plays in an embedded player in the same window.
5. If the owner disabled embedding, an **«Open on YouTube»** link is shown.

Movies are matched by **title and year** from Jellyfin metadata — no separate IDs
(TMDb, Kinopoisk) are required.

---

## Requirements

- Jellyfin Server **10.10.x** or newer
- A free **YouTube Data API v3** key (see «Configuration»)
- YouTube access from the device viewing the web interface

---

## Installation

### Via plugin repository

Works only when this repository and its releases are **public** (Jellyfin downloads the
`.zip` from the manifest URL without authentication).

1. Open **Dashboard → Plugins → Repositories**.
2. Click **+** and add the repository URL:

   ```
   https://raw.githubusercontent.com/dobriys/jellyfin_trailer/main/dist/manifest.json
   ```

3. Go to **Plugin Catalog**, find **Trailer** under **General**.
4. Click **Install** and restart Jellyfin.

### Manual installation

1. Download the latest `Jellyfin.Plugin.Trailer_*.zip` from
   [Releases](https://github.com/dobriys/jellyfin_trailer/releases).
2. Create a plugin folder and extract the `.zip` contents (`.dll` and `.xml`) into it:
   - **Docker:** `/config/data/plugins/Trailer_<version>/`
   - **Linux** (apt/rpm): `/var/lib/jellyfin/plugins/Trailer_<version>/`
   - **Windows:** `%ProgramData%\Jellyfin\Server\plugins\Trailer_<version>\`
3. Make sure the folder is writable by the Jellyfin process
   (in Docker usually `chown -R 1000:1000`).
4. Restart Jellyfin.

> Keep exactly one `Trailer_*` folder. Remove old versions, otherwise the server may get
> confused when reading the plugin and its configuration.

---

## Configuration

Open **Dashboard → Plugins → Trailer**.

| Setting | Description |
|---|---|
| **YouTube Data API v3 Key** | Key used to search YouTube for trailers. The only plugin setting. |

### Getting a YouTube Data API v3 key

1. Open the [Google Cloud Console](https://console.cloud.google.com/) with your account.
2. Create a project: **Select a project → New Project**.
3. Enable the API on the
   [YouTube Data API v3](https://console.cloud.google.com/apis/library/youtube.googleapis.com)
   page → **Enable** (make sure your project is selected).
4. Create a key: **APIs & Services → Credentials → Create Credentials → API key**.
5. Paste the key into the plugin settings and click **Save**.

The free quota is 10,000 units per day; one search costs 100 units (about 100 searches/day).

> **Key restrictions.** The plugin calls the API **from the server**, so in the key settings:
> - **Application restrictions** — **None** or **IP addresses** (the server's public IP),
>   but **not** «HTTP referrers» (that blocks server-side requests).
> - **API restrictions** — «Don't restrict», or restricted with **YouTube Data API v3** enabled.
>
> Paste the key cleanly: fully clear the field (Ctrl+A → Delete), dismiss any browser
> autofill suggestions, and paste exactly the key with no extra characters.

---

## Adding the button to the interface

The button is added via JavaScript. Ready-to-copy snippets are available in the plugin
settings: **Dashboard → Plugins → Trailer**.

### Method 1 — `custom.js` file (recommended)

Jellyfin automatically loads `custom.js` from the web interface folder if it exists.

| Installation | File path |
|---|---|
| **Docker** (`jellyfin/jellyfin`) | `/jellyfin/jellyfin-web/custom.js` |
| **Docker** (linuxserver) | `/usr/share/jellyfin/web/custom.js` |
| **Linux** (apt/rpm/tar) | `/usr/share/jellyfin/web/custom.js` |
| **Windows** | `C:\Program Files\Jellyfin\Server\jellyfin-web\custom.js` |
| **Synology DSM** | `/volume1/@appstore/Jellyfin/package/jellyfin-web/custom.js` |

File content (replace the URL with your Jellyfin address):

```js
(function(){
  var s = document.createElement('script');
  s.src = 'https://YOUR_JELLYFIN/web/configurationpage?name=trailerPlugin_js';
  document.head.appendChild(s);
})();
```

> **Docker:** if the `jellyfin-web` folder is not mounted as a volume, the file is wiped on
> every image update. Mount the folder as a volume or copy the file again:
> ```bash
> docker cp custom.js jellyfin:/jellyfin/jellyfin-web/custom.js
> ```

After creating the file, restart Jellyfin and refresh the browser (Ctrl+Shift+R).

### Method 2 — Nginx reverse proxy

If Jellyfin is behind Nginx, add a `sub_filter` directive to the `location` block:

```nginx
location / {
    proxy_pass http://localhost:8096;

    sub_filter '</head>'
      '<script src="http://localhost:8096/web/configurationpage?name=trailerPlugin_js"></script></head>';
    sub_filter_once on;
    sub_filter_types text/html;
}
```

---

## Usage

After setup, the **«Trailer»** button appears on each movie page next to the playback
buttons (in the browser).

- Clicking the button opens the list of found trailers.
- Selecting a video starts the embedded player in the same window.
- The player closes with the close button, a click on the backdrop, or the `Escape` key.
- If no trailers are found, a «Трейлеры не найдены» message is shown.

---

## Compatibility

| Component | Value |
|---|---|
| Jellyfin Server | 10.10.x and above |
| .NET Runtime | 8.0 |
| Client | Web interface in a browser only (Chrome, Firefox, Safari, Edge) |
| Native apps | Not supported (see «Key characteristic») |

---

## License

[MIT](LICENSE)
