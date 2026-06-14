# AdaVoice — План реалізації

**Питання, на яке це відповідає:** *як саме мені будувати кожну фазу — проєкти, модулі,
інтерфейси, файли і порядок, у якому їх писати?*

Це шар **деталей виконання**. Він не переказує таймлайн чи перешкоди go/no-go —
вони живуть у [MVP roadmap](../roadmaps/mvp-roadmap.md) (стратегія). Він будується на
архітектурі в [design 03](../design/03-architecture.md), device seams у
[design 08](../design/08-testing.md), аудіорушії в [design 06](../design/06-audio-engine.md)
і storage у [design 04](../design/04-data-storage.md).

> **Статус:** не розпочато — ще немає файлів solution чи проєкту. Починається після
> go/no-go Phase 0 (див. [handoff.md](../../handoff.md)).

---

## 1. Структура solution

Тримати кількість проєктів низькою (solo dev), але відокремити **аудіоядро від WPF**, щоб ядро
було тестованим без UI. Рекомендований макет:

```
AdaVoice.sln
├── src/
│   ├── AdaVoice.Core/        Domain + audio core + app services. NO WPF reference.
│   │                         Hardware only behind IAudioCaptureDevice / IAudioRenderDevice.
│   │   ├── Domain/           Phrase, Category, Settings, engine state enum
│   │   ├── Audio/            AudioEngine, MicPassthrough, PhrasePlayer, Recorder, DeviceMonitor
│   │   ├── Audio.Naudio/     Production device impls (WasapiCapture/Out wrappers) + ducking interop
│   │   ├── Storage/          JSON repository (atomic write), BackupService
│   │   └── Services/         PhraseLibraryService, SettingsService, HotkeyService, LocalizationService
│   └── AdaVoice.App/         WPF: Views (XAML), ViewModels, .resx (uk/pl/en), DI wiring, entry point
└── tests/
    ├── AdaVoice.Core.Tests/      State machine, mixer, services (fake devices)
    ├── AdaVoice.Dsp.Tests/       Golden-file DSP (trim, loudness, fade)
    └── AdaVoice.Storage.Tests/   Atomic write, corruption, orphan, import
```

**Чому такий поділ:** ядро — це критична для надійності частина, і воно має працювати в CI проти fake-пристроїв
([design 08 §1](../design/08-testing.md)). WPF нелегко запустити headless у CI, тому
тримання його в окремому проєкті, який *посилається* на Core (ніколи навпаки), захищає тестовий
цикл. `Audio.Naudio` — це підпапка, а не окремий проєкт, щоб уникнути розростання проєктів — але
це єдине місце, де з'являються `WasapiCapture`/`WasapiOut`.

**Напрям залежностей (має триматися):** `App → Core`. Core ніколи не посилається на App чи WPF.
Storage і Services ніколи не посилаються на impl пристроїв Audio напряму — лише на інтерфейси.

## 2. Принципи порядку збірки

1. **Seams перед реалізаціями.** Спершу визначити `IAudioCaptureDevice` / `IAudioRenderDevice` /
   `IDeviceMonitor`, потім зробити fake-и, потім реальні обгортки NAudio. Це дозволяє писати і тестувати
   рушій до того, як торкатися обладнання.
2. **Тест разом з функцією.** Кожен компонент випускається зі своїми тестами в тій самій фазі
   ([design 08 §5](../design/08-testing.md)) — покриття не відкладається.
3. **Найризикованіше першим.** Аудіорушій (Phase 1) будується перед storage і UI, бо
   саме там проєкт живе або вмирає.
4. **Перевикористати єдиний keeper зі spike.** `spike/AdaVoice.Spike/DuckingOptOut.cs` — це референс
   для interop `SetDuckingPreference` — портувати його в `Audio.Naudio`. Усе інше в
   `spike/` — викидне.

## 3. Кроки збірки фаза за фазою

Фази та їхні критерії виходу визначено в [roadmap](../roadmaps/mvp-roadmap.md). Нижче
*що будувати і в якому порядку* всередині кожної.

### Phase 0 — Spike (код готовий; виконання очікується)

- Код існує в `spike/`. Залишена робота — це **запустити його** на обладнанні — див.
  [spike/README.md](../../spike/README.md) і [handoff.md](../../handoff.md). Без виробничого
  коду в цій фазі.

### Phase 1 — Аудіоядро + тести

Порядок збірки:

1. **Solution + проєкти + CI** — створити макет вище; підключити CI workflow, що ганяє
   unit + golden-file набори на кожному коміті (обов'язково з Phase 1).
2. **Domain-типи** — engine state enum (`Stopped / Live / OffAir / Degraded`), value-типи
   для форматів і рівнів.
3. **Device seams** — `IAudioCaptureDevice`, `IAudioRenderDevice`, `IDeviceMonitor`
   ([design 08 §1](../design/08-testing.md)) + test doubles: `FileCaptureDevice`,
   `MemoryRenderDevice`, `FaultyDevice`, синтетичний `IDeviceMonitor`.
4. **`PhrasePlayer` + mixer** — правило одного відтворення, стоп-fade 10 ms, рампа приглушення (duck). Тест проти
   `MemoryRenderDevice` (golden files для fade).
5. **`MicPassthrough`** — capture → mono/48k → гілка приглушення (duck) → mixer.
6. **`AudioEngine`** — володіє трьома потоками, машиною станів, watchdog (перебудова при
   зависанні запиту >500 ms), політикою дрейфу (drop-oldest / insert-silence, залоговано), тривогою DEGRADED на
   системному пристрої за замовчуванням, `RegisterApplicationRestart`. Ганяти кожен перехід через
   `FaultyDevice`.
7. **`Recorder`** (DSP) — trim, узгодження гучності RMS до каліброваного референсу (пікова стеля
   −3 dBFS), забезпечення OFF AIR. Golden-file тести.
8. **Виробничі impl `Audio.Naudio`** — обгорнути `WasapiCapture`/`WasapiOut`; портувати interop відмови
   від ducking зі spike. Викликати відмову на кожному (пере)старті потоку.
9. **8-годинний soak** на реальному обладнанні (тільки рушій, без UI) — події дрейфу < кількох/годину;
   від'єднання/під'єднання відновлюється.

### Phase 2 — Бібліотека + storage

1. `IPhraseRepository` + JSON-реалізація з **атомарним записом** (tmp + rename).
2. Перевірка під час старту + шлях відновлення (пошкоджений `library.json` → завантажити найновішу копію, показати
   повідомлення, ніколи тихо не стартувати порожнім).
3. Orphaning-видалення (`deleted-{id}.wav`), позначення зламаної фрази при відсутньому/пошкодженому WAV.
4. `BackupService` — щоденний zip із `audio/` (тримати 7); ручний export/import (orphan виключено
   з експорту).
5. Тести: автоматизована симуляція kill-9, відновлення після пошкодження, round-trip export→import.

### Phase 3 — Борд + UI Recorder + локалізація

1. DI/bootstrap у `AdaVoice.App`; `StatusViewModel`, прив'язаний до подій стану рушія.
2. Спершу хребет локалізації — `.resx` для uk/pl/en + тест повноти; **без жорстко закодованих
   рядків XAML з першого view і далі**.
3. `BoardViewModel` + view Board — великі кнопки фраз (активуються в міру фонового декодування),
   перемикач Topmost (за замовчуванням увімкнено), рядок статусу, великий STOP. Будувати **макети Full і Docked** на
   токенах [design 09](../design/09-design-system.md).
4. `RecorderViewModel` + view Recorder — банер OFF AIR, запис/перезапис, попереднє прослуховування в monitor.
5. Усі стани взаємодії з [design 05 §2](../design/05-ui-design.md) (привітання при першому запуску,
   decode-dimmed, зламана фраза, порожній пошук/категорія, toasts).
6. **Пілот оператора** (½ дня) після цієї фази — єдина перешкода прийняття перед пізніми фазами.

### Phase 4 — Гаряча клавіша стопу + Settings + майстер

1. `HotkeyService` — `RegisterHotKey` через `HwndSource`; за замовчуванням `Pause` + запасний `Ctrl+F12`;
   конфлікт показано як типізовану помилку.
2. `SettingsViewModel` + згрупований IA Settings (Levels → Behavior → Language & Backup → Devices
   з підтвердженням при зміні); живі повзунки приглушення (duck), метри пристроїв, перезапуск калібрування.
3. Setup wizard — усі перевірки середовища, loopback-самотест, картка впевненості першого дзвінка
   (рішення #24).

### Phase 5 — Загартовування + інсталятор

1. Граничні випадки з [design 07](../design/07-risks-security.md); Serilog rolling-file логування +
   тривоги стану рушія.
2. Inno Setup self-contained .NET 10 інсталятор; короткий посібник користувача (збірник запасних дій, скриншоти
   мікрофона Zoho, нота SmartScreen).
3. Фінальний чек-лист ручного тесту дзвінка ([design 08 §4](../design/08-testing.md)); фоллоу-ап пілоту.
4. → передати [плану готовності до продакшну](production-readiness-plan.md) для перешкоди релізу.

## 4. Наскрізні правила (застосовуються в кожній фазі)

- Жодної аудіороботи на WPF dispatcher; UI↔рушій через незмінні команди + marshalled-події
  ([design 03 §4](../design/03-architecture.md)).
- Пристрої зберігаються за MMDevice ID із запасним варіантом за дружньою назвою — ніколи не вгадувати при перенумерації.
- Кожен рядок для користувача проходить через `.resx` з першого дня.
- Код фази не зливається без своїх зелених тестів у CI.
