# Jellyfin Trailer Plugin

> **Vibe coding:** этот плагин полностью написан с помощью vibe coding в паре с Claude (Cursor).

Плагин добавляет кнопку **«Трейлер»** на страницу каждого фильма в вашей библиотеке Jellyfin.
Трейлер воспроизводится прямо в интерфейсе Jellyfin — без перехода на YouTube.

![Jellyfin](https://img.shields.io/badge/Jellyfin-10.10%2B-blue?logo=jellyfin)
![.NET](https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet)
![License](https://img.shields.io/github/license/dobriys/jellyfin_trailer)

---

## Возможности

- 🎬 Кнопка **«Трейлер»** появляется на странице каждого фильма в библиотеке
- ▶️ **Встроенный плеер** — трейлер открывается в оверлее прямо в Jellyfin, без ухода с сайта
- 🔗 Альтернативно — открытие трейлера в новой вкладке (YouTube)
- 🌐 Источник трейлеров: **TMDb** (основной) + **Kinopoisk Unofficial API** (запасной)
- 🇷🇺 Поддержка русского языка — сначала ищет русскоязычный трейлер, при отсутствии — английский
- ⚡ Кэширование результатов — повторные запросы мгновенны

---

## Установка

### Через репозиторий плагинов (рекомендуется)

1. Откройте **Панель управления → Плагины → Репозитории**
2. Нажмите **+** и добавьте адрес репозитория:

   ```
   https://raw.githubusercontent.com/dobriys/jellyfin_trailer/main/dist/manifest.json
   ```

3. Перейдите в **Каталог плагинов**, найдите **Trailer** в категории **General**
4. Нажмите **Установить** и перезапустите Jellyfin

### Ручная установка

1. Скачайте последний `Jellyfin.Plugin.Trailer_*.zip` со страницы [Releases](https://github.com/dobriys/jellyfin_trailer/releases)
2. Распакуйте содержимое `.zip` в папку плагинов Jellyfin:
   - **Linux / Docker:** `/config/plugins/` или `~/.local/share/jellyfin/plugins/`
   - **Windows:** `%APPDATA%\Jellyfin\plugins\`
3. Перезапустите Jellyfin

---

## Настройка

Откройте **Панель управления → Плагины → Trailer**.

| Параметр | Описание |
|---|---|
| **Режим воспроизведения** | `Встроенный плеер` — трейлер в оверлее Jellyfin / `Новая вкладка` — открывает YouTube |
| **TMDb API Key** | Ключ от [themoviedb.org](https://www.themoviedb.org/settings/api) — основной источник трейлеров |
| **Язык трейлера** | Предпочтительный язык поиска: русский, английский, украинский |
| **Английский как запасной** | Если нет трейлера на выбранном языке — искать на en-US |
| **Kinopoisk API Key** | Токен от [kinopoiskapiunofficial.tech](https://kinopoiskapiunofficial.tech) — запасной источник |
| **Включить Kinopoisk** | Использовать Kinopoisk, если TMDb не нашёл трейлер |
| **Время кэширования** | Сколько минут хранить результат в памяти (0 — отключить) |

### Получение TMDb API Key

1. Зарегистрируйтесь на [themoviedb.org](https://www.themoviedb.org)
2. Перейдите в **Настройки → API** и запросите ключ (бесплатно)
3. Скопируйте **API Key (v3 auth)** в поле настроек плагина

### Получение Kinopoisk API Key

1. Зарегистрируйтесь на [kinopoiskapiunofficial.tech](https://kinopoiskapiunofficial.tech)
2. Получите бесплатный API-токен (лимит ~500 запросов/день)
3. Вставьте токен в соответствующее поле настроек плагина

> **Важно:** Kinopoisk Unofficial API — неофициальный сервис, его работоспособность не гарантирована.

---

## Подключение кнопки к интерфейсу

Кнопка **«Трейлер»** добавляется через JavaScript. Выберите подходящий способ.

> 💡 Готовые сниппеты для копирования доступны прямо в настройках плагина:
> **Панель управления → Плагины → Trailer**

---

### Способ 1 — файл `custom.js` ✅ рекомендуется

Jellyfin автоматически загружает файл `custom.js` из папки веб-интерфейса, если он существует.

**Создайте файл** по нужному пути и поместите в него содержимое из настроек плагина (раздел «Подключение кнопки»):

| Установка | Путь к файлу |
|---|---|
| **Docker** (`jellyfin/jellyfin`) | `/jellyfin/jellyfin-web/custom.js` |
| **Linux** (apt/rpm/tar) | `/usr/share/jellyfin/web/custom.js` |
| **Windows** | `C:\Program Files\Jellyfin\Server\jellyfin-web\custom.js` |
| **Synology DSM** | `/volume1/@appstore/Jellyfin/package/jellyfin-web/custom.js` |

Содержимое файла (замените URL на адрес вашего Jellyfin):

```js
(function(){
  var s = document.createElement('script');
  s.src = 'https://ВАШ_JELLYFIN/web/configurationpage?name=trailerPlugin_js';
  document.head.appendChild(s);
})();
```

> **Docker:** если папка `jellyfin-web` не примонтирована как volume, нужно либо пробросить её,
> либо скопировать файл командой:
> ```bash
> docker cp custom.js jellyfin:/jellyfin/jellyfin-web/custom.js
> ```
> После обновления контейнера файл нужно скопировать снова — используйте volume для постоянства.

После создания файла **перезапустите Jellyfin** и обновите страницу браузера (Ctrl+Shift+R).

---

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

Готовый сниппет с правильным URL также доступен в настройках плагина.

---

## Использование

После настройки кнопка **«Трейлер»** появляется на странице каждого фильма рядом с кнопками воспроизведения.

- **Клик по кнопке** → трейлер открывается во встроенном плеере (или на YouTube — зависит от настройки)
- **Встроенный плеер** закрывается кнопкой ✕, кликом по фону или клавишей `Escape`
- Если трейлер для фильма не найден — кнопка не отображается

Плагин автоматически определяет фильм по **TMDb ID**, который Jellyfin берёт при сканировании библиотеки. Для этого в библиотеке должны быть включены метаданные TMDb.

---

## Совместимость

| Компонент | Версия |
|---|---|
| Jellyfin Server | 10.10.x и выше |
| .NET Runtime | 8.0 |
| Браузер | Любой современный (Chrome, Firefox, Safari, Edge) |

---

## Лицензия

[MIT](LICENSE)

---

# English

> **Vibe coding:** this plugin was written entirely using vibe coding with Claude (Cursor).

The plugin adds a **«Trailer»** button to every movie page in your Jellyfin library. Trailers play inside the Jellyfin interface — no need to leave for YouTube.

---

## Features

- 🎬 **«Trailer»** button appears on each movie page in the library
- ▶️ **Embedded player** — trailer opens in an overlay inside Jellyfin, without leaving the site
- 🔗 Alternatively — open the trailer in a new tab (YouTube)
- 🌐 Trailer sources: **TMDb** (primary) + **Kinopoisk Unofficial API** (fallback)
- 🇷🇺 Russian language support — searches for Russian trailer first, then English if none found
- ⚡ Result caching — repeated requests are instant

---

## Installation

### Via plugin repository (recommended)

1. Open **Dashboard → Plugins → Repositories**
2. Click **+** and add the repository URL:

   ```
   https://raw.githubusercontent.com/dobriys/jellyfin_trailer/main/dist/manifest.json
   ```

3. Go to **Plugin Catalog**, find **Trailer** under **General**
4. Click **Install** and restart Jellyfin

### Manual installation

1. Download the latest `Jellyfin.Plugin.Trailer_*.zip` from [Releases](https://github.com/dobriys/jellyfin_trailer/releases)
2. Extract the contents of the `.zip` into the Jellyfin plugins folder:
   - **Linux / Docker:** `/config/plugins/` or `~/.local/share/jellyfin/plugins/`
   - **Windows:** `%APPDATA%\Jellyfin\plugins\`
3. Restart Jellyfin

---

## Configuration

Open **Dashboard → Plugins → Trailer**.

| Setting | Description |
|---|---|
| **Playback mode** | `Embedded player` — trailer in Jellyfin overlay / `New tab` — opens YouTube |
| **TMDb API Key** | Key from [themoviedb.org](https://www.themoviedb.org/settings/api) — primary trailer source |
| **Trailer language** | Preferred search language: Russian, English, Ukrainian |
| **English as fallback** | If no trailer in selected language — search in en-US |
| **Kinopoisk API Key** | Token from [kinopoiskapiunofficial.tech](https://kinopoiskapiunofficial.tech) — fallback source |
| **Enable Kinopoisk** | Use Kinopoisk when TMDb doesn’t find a trailer |
| **Cache duration** | How many minutes to keep results in memory (0 — disable) |

### Getting TMDb API Key

1. Register at [themoviedb.org](https://www.themoviedb.org)
2. Go to **Settings → API** and request a key (free)
3. Copy **API Key (v3 auth)** into the plugin settings

### Getting Kinopoisk API Key

1. Register at [kinopoiskapiunofficial.tech](https://kinopoiskapiunofficial.tech)
2. Get a free API token (limit ~500 requests/day)
3. Paste the token into the corresponding field in the plugin settings

> **Note:** Kinopoisk Unofficial API is an unofficial service; its availability is not guaranteed.

---

## Adding the button to the interface

The **«Trailer»** button is added via JavaScript. Choose the method that fits your setup.

> 💡 Ready-to-copy snippets are available in the plugin settings:
> **Dashboard → Plugins → Trailer**

---

### Method 1 — `custom.js` file ✅ recommended

Jellyfin automatically loads `custom.js` from the web interface folder if it exists.

**Create the file** at the path for your installation and paste the content from the plugin settings (section «Adding the button»):

| Installation | File path |
|---|---|
| **Docker** (`jellyfin/jellyfin`) | `/jellyfin/jellyfin-web/custom.js` |
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

> **Docker:** if the `jellyfin-web` folder is not mounted as a volume, you need to either mount it or copy the file with:
> ```bash
> docker cp custom.js jellyfin:/jellyfin/jellyfin-web/custom.js
> ```
> After updating the container, copy the file again — use a volume for persistence.

After creating the file, **restart Jellyfin** and refresh the browser (Ctrl+Shift+R).

---

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

A ready snippet with the correct URL is also available in the plugin settings.

---

## Usage

After setup, the **«Trailer»** button appears on each movie page next to the playback buttons.

- **Click the button** → trailer opens in the embedded player (or on YouTube — depends on settings)
- **Embedded player** closes with the ✕ button, click on the backdrop, or the `Escape` key
- If no trailer is found for the movie — the button is not shown

The plugin identifies the movie by **TMDb ID**, which Jellyfin gets when scanning the library. TMDb metadata must be enabled for the library.

---

## Compatibility

| Component | Version |
|---|---|
| Jellyfin Server | 10.10.x and above |
| .NET Runtime | 8.0 |
| Browser | Any modern (Chrome, Firefox, Safari, Edge) |

---

## License

[MIT](LICENSE)
