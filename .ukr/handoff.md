# AdaVoice — Передача контексту та прогрес

**Поточний статус проєкту.** Прочитай це першим, коли ти або нова сесія повертаєтесь до роботи. Документ відповідає на одне питання: *де ми зараз?*

- **Що це:** завершена робота, робота в процесі, усе, що було перервано, та відкриті питання.
- **Що це не:** план (див. [implementation plan](docs/plans/implementation-plan.md)),
  стратегія (див. [roadmap](docs/roadmaps/mvp-roadmap.md)) або журнал рішень
  (канонічна таблиця в [design 01 §4](docs/design/01-overview.md#4-confirmed-decisions-canonical)).

_Останнє оновлення: 2026-06-15._

---

## Статус одним реченням

**Дизайн завершено та перевірено. Phase 0 go/no-go gate ПРОЙДЕНО на цільовій машині
(Architecture A підтверджено). Phase 1 audio core частково зібрано, а WASAPI seam
перевірено на реальному обладнанні.** Наступний реальний крок: завершити Phase 1 engine
(orchestrator, recorder, device monitor).

## Завершено

- ✅ **Design phase** — 9 документів у [`docs/design/`](docs/design/README.md), eng review + design
  review обидва ПРОЙДЕНО (2026-06-10).
- ✅ **Canonical decisions** — 24 записи зафіксовано ([design 01 §4](docs/design/01-overview.md#4-confirmed-decisions-canonical)).
- ✅ **A8 permission gate** — вирішено 2026-06-13: угода з роботодавцем/Zoho не потрібна
  (роботодавець лояльний). Більше не блокує розробку.
- ✅ **Phase 0 spike — code** — одноразовий консольний прототип закомічено в [`spike/`](spike/README.md)
  (mic→duck→mix→CABLE, latency self-test, ducking opt-out interop). ~656 рядків. Збирається для
  `net10.0-windows`.
- ✅ **Phase 0 go/no-go gate — ПРОЙДЕНО (2026-06-15)** — протестовано end-to-end на реальному
  Zoho Voice дзвінку на цільовій машині. Architecture A підтверджено; відпрацьований fallback
  через Voicemeeter залишається задокументованим планом B, але не потрібен. (Див. "Відкриті питання" —
  A5/A6/A11 вирішено.) _Детальні числа (mouth-to-Chrome latency, AGC notes) ще потрібно
  зафіксувати в `spike/PHASE0-RESULTS.md` — файл ще не створено._
- ✅ **Phase 1 audio core — частково (2026-06-15)** — production code у [`src/`](src/):
  device seams (`IAudioCaptureDevice`/`IAudioRenderDevice`), `MicPassthrough` (capture →
  format → duck), `PhrasePlayer` + `PhraseSampleProvider` (single-playback, fade-out),
  `RampGain`/`ChannelAdapter` DSP, а також WASAPI seam (`WasapiCaptureDevice`,
  `WasapiRenderDevice`, `DuckingOptOut` COM interop). 23 unit tests green на fake devices;
  CI збирає + запускає тести на кожен push. Seam перевірено на реальному обладнанні через
  [`tools/AudioSeamCheck`](tools/AudioSeamCheck) (live mic→CABLE passthrough with ducking).
- ✅ **Doc structure cleanup** (2026-06-13) — видалено початковий brief (`1_DESIGN.md`) і
  `TODOS.md`; дизайн-систему перенесено в [`docs/design/09-design-system.md`](docs/design/09-design-system.md);
  планувальні документи додано в [`docs/plans/`](docs/plans/).

## У процесі / перервано

- _Нічого активно не виконується._ Проєкт поставлено на паузу всередині Phase 1, після зрізу
  audio-core (seams + passthrough + player) і перед engine orchestrator.

## Наступна дія

**Продовжити Phase 1 — побудувати `AudioEngine` orchestrator поверх перевірених seams.**
Згідно з [roadmap Phase 1](docs/roadmaps/mvp-roadmap.md) і [design 06](docs/design/06-audio-engine.md):
state machine (Stopped/Live/OffAir/Degraded), watchdog (render-pull stall → rebuild),
`DeviceMonitor` (`IMMNotificationClient` device-loss recovery), drift logging (overrun count
+ underrun count ще не surfaced — див. code note в `MicPassthrough`), `Recorder`
(trim + RMS loudness-match + OFF AIR), DEGRADED alarm на system default device, і
`RegisterApplicationRestart`. Менше doc-завдання все ще відкрите: створити `spike/PHASE0-RESULTS.md`
з виміряними Phase 0 numbers.

## Відкриті питання

Технічні невизначеності Phase 0 тепер **вирішено** завдяки пройденому gate (2026-06-15):

- ✅ **A5** — Zoho/Chrome поважає вибір мікрофона `CABLE Output`. *(підтверджено на реальному дзвінку)*
- ✅ **A6** — Chrome **AGC** пропускає попередньо записані фрази зрозуміло. *(підтверджено;
  точні AGC/level notes зафіксувати в `spike/PHASE0-RESULTS.md`)*
- ✅ **A11** — mouth-to-Chrome latency є прийнятною end-to-end. *(підтверджено; записати
  виміряне число в `spike/PHASE0-RESULTS.md`)*
- ✅ `SetDuckingPreference` opt-out тримається після повторних циклів start/stop дзвінка.

## Відкладені / заблоковані пункти

- 🔒 **Board design mockups** — запустити gstack designer для 3 dark-theme Board варіантів
  (Full + Docked) згідно з [design 09](docs/design/09-design-system.md). **Заблоковано через OpenAI
  API key** (`~/.gstack/openai.json` або `OPENAI_API_KEY`). Найкраще зробити до того, як Phase 3
  будуватиме Board у XAML. _(Перенесено зі старого TODOS.md, 2026-06-10.)_
- Post-MVP backlog знаходиться в [roadmap](docs/roadmaps/mvp-roadmap.md#deferred