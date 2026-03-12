# Jellyfin Trailer Plugin

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
