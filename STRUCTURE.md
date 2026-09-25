# Структура проекта SoA

## Правило раскладки

1. **Верхний уровень — по типу контента** (как ждёт tML и как устроены пути к ассетам):
   `Content/Items`, `Content/Projectiles`, `Content/NPCs`, `Content/Tiles`, `Content/Buffs`.
2. **Внутри типа — подпапка на владельца**, если файлов больше одного:
   `Content/Projectiles/ScytheOfFireStorm/`, `Common/Players/RoyalSpear/`.
3. **Пара «код + одноимённый спрайт» — это один файл, а не группа**: `Aquasaw.cs` +
   `Aquasaw.png` лежат плоско в `Content/Items/Weapons`, папку под них не создаём.
4. **Спрайт едет вместе с кодом.** Отсюда единственная ловушка — см. ниже.

## ⚠️ Ловушка: путь к спрайту берётся из пространства имён

tML ищет текстуру по `namespace + имя класса`, **не по расположению файла**. Пространства
имён здесь не совпадают с папками (иначе `namespace ...Projectiles.RoyalSpear` конфликтовал
бы с классом `RoyalSpear` — `CS0118`), поэтому у каждого класса, чей спрайт лежит в
подпапке владельца, стоит явный оверрайд:

```csharp
public override string Texture => "SoA/Content/Projectiles/Aquasaw/AquasawProjectile";
```

Забыл оверрайд — компилятор промолчит, а в игре будет «Asset could not be found».
Проверка путей — скриптом из истории задач (обходит все строки `"SoA/..."` и все
текстурные классы без оверрайда).

## Где что лежит

### Content — игровой контент

| Папка | Что внутри |
|---|---|
| `Items/Weapons` | оружие; парами код+спрайт |
| `Items/Accessories/<Аксессуар>/` | аксессуар + его ModPlayer + слои отрисовки |
| `Items/Ammo`, `Items/Materials`, `Items/Placebles`, `Items/Fishing`, `Items/BossSummons`, `Items/Critters`, `Items/Armor` | предметы по назначению |
| `Items/DevTools` | палочка структур, её рендерер и `icons/` |
| `Items/oldtextures` | старый арт, в игре не используется |
| `Projectiles/<Владелец>/` | снаряды, сгруппированные по оружию/боссу, которому принадлежат |
| `NPCs/Bosses/KingCrab` | босс: `King_crab.*.cs` — части одного класса (Claws, Crown, Legs, Vfx, Rig) |
| `NPCs/Enemies`, `NPCs/Critters`, `NPCs/Friendly_NPCs` | остальные NPC |
| `Tiles/Nature`, `Tiles/Ores`, `Tiles/Other` | тайлы |
| `Buffs` | баффы и дебаффы; `LavaExplosion/` — дебафф вместе со своим GlobalNPC |
| `Water` | стиль воды Прилива Теней |
| `Worldgen` | биом |
| `Sounds` | звуки |

### Common — механика и инфраструктура

| Папка | Что внутри |
|---|---|
| `Players` | ModPlayer'ы; `RoyalSpear/` — состояние копья и полоска приливного удара |
| `Systems` | `SoASystem` (регистрация шейдеров), `DownedBossSystem` |
| `Systems/Worldgen` | генерация: проходы, `TideOcean/` — океан Прилива Теней |
| `Graphics` | `SoAVfx` (примитивы поверх шейдеров), `TideGeyserFx` (гейзер), эффекты рёва и камеры, `Animation/` |
| `UI/DevMenu`, `UI/StructureTool` | самодельный интерфейс инструментов разработки |
| `UI` (плоско) | `SoAHudDraw`, `ToolText` — общие примитивы рисования интерфейса |
| `Utils` | `SoACombat` (проверка «промок»), структуры, шум, фильтры |
| `Config`, `CustomClasses`, `Backgrounds` | по одному файлу на назначение |

### Assets — то, что не привязано к классу

| Папка | Что внутри |
|---|---|
| `Effects` | шейдеры: `.fx` (источник), `.fxo`, `.xnb` (то, что грузит игра), `Compiler/fxc.exe` |
| `Textures` | текстуры для шейдеров и интерфейса: `WaveNoise`, `BeamDistortion`, `TideGeyser` (лист струи), `RoyalTideBar*` |
| `Dusts`, `Gores`, `Structures` | пыль, горы, файлы структур |

## Общие компоненты — не растаскивать по владельцам

`SoAVfx`, `TideGeyserFx`, `SoACombat` намеренно лежат в `Common`: гейзер заявлен под биом
и босса, проверка «промок» используется четырьмя оружиями. Копия в папке оружия — прямой
путь к дублированию логики.

## Мусор, который стоит однажды разобрать

- `Content/Projectiles/death_Breathe.png` — в коде не упоминается
- `Content/Items/Weapons/Toothbreaker.png` — спрайт без класса
- `Content/Items/oldtextures/` — старый арт
- `Assets/Effects/obj/` — остатки mgcb, к сборке не нужны
