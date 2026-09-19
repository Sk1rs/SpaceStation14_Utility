# SS14 MIDI плеер

Настольный плеер MIDI, который звучит так же, как инструменты в билде МК
([dead-space-server/space-station-14-soyuz](https://github.com/dead-space-server/space-station-14-soyuz)),
чтобы слушать и подбирать миди, не заходя в игру.

Внутри тот же синтезатор (fluidsynth через `SpaceWizards.NFluidsynth` и `Robust.Natives.Fluidsynth` —
ровно те пакеты, что тянет клиент игры), те же саундфонты и портированная логика инструментов из
`Content.Client/Instruments` и `RobustToolbox/Robust.Client/Audio/Midi`.

## Запуск

```bash
dotnet run -c Release --project SS14MidiPlayer/SS14MidiPlayer.csproj
```

Или собрать и запустить exe напрямую:

```bash
dotnet build -c Release SS14MidiPlayer/SS14MidiPlayer.csproj
"SS14MidiPlayer/bin/Release/net8.0-windows/win-x64/SS14MidiPlayer.exe"
```

Файлы можно бросать в окно перетаскиванием, папку — тоже.

## Что умеет

**Инструменты.** Все 70 сущностей билда с компонентом `Instrument` — от рояля и скрипки до
велосипедного клаксона, КПК музыканта и суперсинтезатора. Для каждого берутся ровно те поля, что
стоят в прототипе: `program`, `bank`, `allowPercussion`, `allowProgramChange`, `respectMidiLimits`.
Названия — русские из локализации игры.

**Стили.** У 12 инструментов есть `SwappableInstrument` (то самое ПКМ-меню в игре): гитара
Clean/Jazz/Muted, бас Fingered/Pick/Slap, саксофон Soprano/Alto/Tenor/Baritone, микрофон с
Kweh/Waa/Wah из кастомного банка 100 и так далее. Стили выбираются выпадающим списком.

**Логика воспроизведения — порт `MidiRenderer.SendMidiEvent`:**

- когда `allowProgramChange: false` (а это почти все инструменты), команды смены программы и банка из
  файла игнорируются, а на все каналы кроме ударного принудительно ставится программа инструмента —
  поэтому в игре скрипка играет скрипкой весь файл;
- когда `allowPercussion: false`, канал 9 (десятый, ударный) глушится;
- `SystemReset` из файла возвращает инструмент на его собственную программу, а не на ту, что записана
  в миди;
- фильтрация любых каналов — как меню каналов в игре, с названиями треков и именами GM-программ,
  вытащенными тем же парсером (`Content.Client/Instruments/MidiParser`).

**Ручной режим.** Program 0–127, Bank и список GM-инструментов доступны напрямую — можно услышать
любую программу, даже ту, которой нет ни у одного игрового инструмента. Кнопка «Сбросить к прототипу»
возвращает настройки инструмента.

**«Играть как слышат другие».** Локально исполнитель всегда слышит файл целиком, а по сети идёт
ограниченный поток: `midi.max_events_per_batch` (60 событий за тик), `midi.max_events_per_second`
(1000) и `midi.max_lagged_batches` (8), после чего сервер перестаёт ретранслировать музыку, а игроку
сводит пальцы и оглушает. Галка прогоняет воспроизведение через ту же очередь: видно события в
секунду, длину очереди, задержку и момент, когда музыка оборвалась бы. Удобно проверять, потянет ли
игра плотный миди. У суперсинтезатора админский `respectMidiLimits: false`, и лимиты на него не
действуют — здесь тоже.

**Прочее.** Повтор, пауза, перемотка, громкость, «Паника» (сброс зависших нот, аналог `midipanic`),
предупреждение о файлах тяжелее 2 МБ — игра такие не принимает.

## Саундфонты

Грузятся в том же порядке, что и в клиенте (каждый следующий перекрывает предыдущий):

1. `soundfonts/fallback.sf2` — из движка (`Resources/Midi/fallback.sf2`, взят из
   `284.0.2.zip` в кэше лаунчера);
2. `C:\Windows\system32\drivers\gm.dls` — системный; fluidsynth не считает `.dls` саундфонтом и
   пропускает его, так же как и в игре;
3. `soundfonts/GeneralUser-GS.sf2` и `soundfonts/space-station-14.sf2` — из
   `Resources/Audio/MidiCustom` билда МК; второй как раз даёт кастомные банки (клаксон, вокал микрофона);
4. всё, что лежит в `%APPDATA%\Space Station 14\data\soundfonts` — как и в игре, перекрывает остальное.

Кнопка «Саундфонты» показывает, что именно загрузилось.

## Обновление данных из репозитория

Каталог инструментов и названия программ вытащены скриптами `tools/*.py` из клона билда МК
([dead-space-server/space-station-14-soyuz](https://github.com/dead-space-server/space-station-14-soyuz))
и его RobustToolbox. Сами клоны в репозиторий не входят (слишком большие и не нужны для сборки) —
если билд обновится, склонируйте их рядом заново:

```bash
git clone --filter=blob:none https://github.com/dead-space-server/space-station-14-soyuz repo
git -C repo submodule update --init RobustToolbox

python tools/extract_instruments.py repo SS14MidiPlayer/Assets/instruments.json
python tools/extract_programs.py repo SS14MidiPlayer/Assets/programs.json
dotnet build -c Release SS14MidiPlayer/SS14MidiPlayer.csproj
```

Саундфонты копируются вручную из `repo/Resources/Audio/MidiCustom` в `SS14MidiPlayer/soundfonts`.

## Проверка без окна

```bash
SS14MidiPlayer.exe --selftest "путь\к.mid" ViolinInstrument 6 --limits
```

Пишет лог в `%TEMP%\ss14midiplayer-selftest.log`: какие саундфонты загрузились, что за инструмент,
какие треки распознаны, как идёт воспроизведение и что показывает симулятор лимитов. С
`--render out.wav` звук пишется в файл (сырой PCM 16 бит, 44100, стерео) вместо колонок, с
`--maxeps N` лимиты искусственно занижаются, чтобы посмотреть, как звучит обрыв.

## Структура

| Путь | Что это |
| --- | --- |
| `Ss14MidiEngine.cs` | синтезатор: настройки и обработка событий, порт `MidiRenderer` |
| `GameLimitSimulator.cs` | сетевые лимиты клиента и сервера |
| `MidiFileParser.cs` | порт `MidiParser` — названия треков и каналов |
| `InstrumentCatalog.cs` | каталог инструментов и GM-программ |
| `MainForm.cs` | интерфейс |
| `Assets/*.json` | данные, вытащенные из прототипов и локализации |
| `soundfonts/` | саундфонты и их лицензии |
| `tools/*.py` | скрипты извлечения данных из клона билда (см. выше) |

## Лицензия и происхождение данных

Код плеера (`*.cs`) — порт логики движка/клиента SS14, который распространяется по MIT (см. `LICENSE`
в этой папке). `Assets/instruments.json`, `Assets/programs.json` и `soundfonts/space-station-14.sf2`
извлечены из билда [dead-space-server/space-station-14-soyuz](https://github.com/dead-space-server/space-station-14-soyuz)
и являются игровыми ассетами этого проекта (по умолчанию CC-BY-SA 3.0, если не указано иное в его
лицензии) — используйте их с учётом лицензии оригинала. Лицензии остальных саундфонтов лежат рядом
с ними в `soundfonts/`.
