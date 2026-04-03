# 🤖 Robot Repair Station

## A RimWorld BioTech Mod

![RimWorld](https://img.shields.io/badge/RimWorld-1.6-8B4513?style=flat-square)
![BioTech DLC](https://img.shields.io/badge/BioTech_DLC-Required-6A0DAD?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-4.7.2-239120?style=flat-square&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-10.0-239120?style=flat-square&logo=csharp)
![License](https://img.shields.io/badge/License-MIT-blue?style=flat-square)

**Automated repair docking station for BioTech mechanoids.**
When damaged, your mechanoids will autonomously seek out the station and self-repair — no micromanagement required.

---

## ✨ Features

- **Autonomous repair** — Mechanoids detect when their health drops below a configurable threshold and navigate to the nearest available station without player input
- **Resource consumption** — Repair consumes steel from nearby stockpiles, managed via an internal buffer for efficiency
- **Power-gated** — Requires an active electrical connection; station shuts off cleanly when power is lost
- **Manual eject** — A gizmo button lets the player forcibly remove a mechanoid mid-repair
- **Fully configurable** — All parameters (health threshold, repair speed, steel cost, detection range) are editable in the XML with no recompile needed
- **Research gated** — Unlocked by *Mechanoid Repair Systems* (Spacer tier), requiring *Mechanoid Basics* first
- **Breakdown-able** — Requires periodic maintenance, consistent with vanilla industrial buildings
- **Save/load safe** — All state (occupant, steel buffer) is serialized correctly with `Scribe_References` and `Scribe_Values`
- **No lost body parts** — Repairs active injuries only; permanent damage is not restored (by design)

---

## 🏗️ Architecture Overview

The mod is built around four interconnected systems:

```text
┌─────────────────────────────────────────────────────────────────┐
│                        AI LAYER (ThinkTree)                     │
│                                                                 │
│  ThinkNode_ConditionalNeedsRepair                               │
│    └─ Checks: is mechanoid? player-owned? health < threshold?   │
│       └─ JobGiver_GoToRepairStation                             │
│            └─ Emits: RRS_GoToRepairStation job                  │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                        JOB LAYER                                │
│                                                                 │
│  JobDriver_GoToRepairStation                                    │
│    1. GotoThing → walk to InteractionCell                       │
│    2. dock (Instant) → TryAcceptOccupant → enqueue repair job   │
│                                                                 │
│  JobDriver_RepairAtStation                                      │
│    - Wait (ToilCompleteMode.Never)                              │
│    - Ends when CurrentOccupant becomes null                     │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                      BUILDING LAYER                             │
│                                                                 │
│  Building_RobotRepairStation                                    │
│    - Manages occupant (TryAcceptOccupant / EjectOccupant)       │
│    - Tick: TryConsumeSteel every repairTickInterval             │
│    - Steel buffer (up to 50 units) avoids per-tick map searches │
│    - Gizmos, InspectString, save/load                           │
│                                                                 │
│  CompRobotRepairStation (ThingComp)                             │
│    - CompTick: ApplyRepairTick every repairTickInterval         │
│    - Heals all active (non-permanent) Hediff_Injury instances   │
│    - Calls OnRepairComplete when health ≥ 99%                   │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                     REGISTRY LAYER                              │
│                                                                 │
│  RepairStationTracker (MapComponent)                            │
│    - O(1) register/deregister on SpawnSetup / DeSpawn           │
│    - ThinkNodes iterate this list instead of searching the map  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 📁 Folder Structure

```text
RobotRepairStation/
│
├── About/
│   └── About.xml                        ← Mod metadata, packageId, BioTech dependency
│
├── Assemblies/
│   └── RobotRepairStation.dll           ← Compiled output (do not edit manually)
│
├── Defs/
│   ├── JobDefs/
│   │   └── JobDefs_RobotRepair.xml      ← RRS_GoToRepairStation + RRS_RepairAtStation
│   ├── ThingDefs/
│   │   ├── Buildings_RobotRepairStation.xml  ← ThingDef: size, cost, comps, research
│   │   └── ResearchDefs.xml             ← "Mechanoid Repair Systems" (Spacer, 1200pts)
│   ├── StatDefs/                        ← Reserved for future custom stats
│   └── WorkTypeDefs/                    ← Reserved for future work types
│
├── Languages/
│   └── English/
│       └── Keyed/
│           └── RobotRepairStation.xml   ← All player-visible strings (fully localizable)
│
├── Patches/
│   └── MechanoidThinkTree.xml           ← Injects repair node into MechanoidConstant
│
├── Source/
│   └── RobotRepairStation/
│       ├── RobotRepairStation.csproj
│       ├── RRS_Mod.cs                   ← StaticConstructorOnStartup bootstrap
│       ├── RRS_JobDefOf.cs              ← [DefOf] static job references
│       ├── Building_RobotRepairStation.cs   ← Main building: occupant, steel, UI
│       ├── CompProperties_RobotRepairStation.cs  ← Comp + CompProperties (healing tick)
│       ├── JobDriver_GoToRepairStation.cs   ← Walk-to-station job driver
│       ├── JobDriver_RepairAtStation.cs     ← Docked repair job driver
│       ├── ThinkNode_ConditionalNeedsRepair.cs  ← AI conditional + JobGiver + utilities
│       └── RepairStationTracker.cs          ← MapComponent station registry
│
└── Textures/
    └── Things/
        └── Buildings/
            └── RobotRepairStation.png   ← 128×128 building sprite (must be added)
```

---

## ⚙️ Configuration Reference

All parameters are tunable directly in `Defs/ThingDefs/Buildings_RobotRepairStation.xml` inside the `<li Class="RobotRepairStation.CompProperties_RobotRepairStation">` block — no recompile needed.

| Property | Default | Description |
| --- | --- |
| `repairHealthThreshold` | `0.5` | Health fraction (0–1) below which a mechanoid seeks repair. `0.5` = 50%. |
| `repairSpeedPerTick` | `0.0005` | HP restored per game tick to each active injury. Combined with `repairTickInterval`, each cycle heals ~0.25 HP/injury. |
| `steelPerRepairCycle` | `1` | Units of steel consumed per repair interval. At default settings, ~7.2 units/hour. |
| `repairTickInterval` | `500` | Ticks between each steel consumption and healing cycle (~8.3s at ×1 speed). Controls both resource granularity and CPU cost. |
| `maxRepairRange` | `30` | Maximum cell distance for a mechanoid to detect and path to this station. |

> **Tuning tip:** `repairSpeedPerTick` and `repairTickInterval` are coupled. Increasing `repairTickInterval` without adjusting `repairSpeedPerTick` will slow the overall healing rate. The effective HP/second is: `repairSpeedPerTick × 60 / repairTickInterval × repairTickInterval` — simplifies to `repairSpeedPerTick × 60`.

---

## 🔬 How Repair Works (Step by Step)

1. Every AI tick, `ThinkNode_ConditionalNeedsRepair.Satisfied()` checks each player mechanoid:
   - Is it a mechanoid? Is it player-owned? Is health below `repairHealthThreshold`?
   - Is there a powered, unoccupied, reachable station within `maxRepairRange`?

2. If all conditions pass, `JobGiver_GoToRepairStation` emits a `RRS_GoToRepairStation` job targeting the nearest valid station.

3. `JobDriver_GoToRepairStation` walks the mechanoid to the station's `InteractionCell`, then calls `Building_RobotRepairStation.TryAcceptOccupant()` and enqueues `RRS_RepairAtStation`.

4. Every `repairTickInterval` ticks while docked:
   - **Building tick:** `TryConsumeSteel()` deducts from the internal buffer. If the buffer is empty, it searches for steel within 8 cells and reloads up to 50 units. If none found, the mechanoid is ejected and the player is notified.
   - **Comp tick:** `ApplyRepairTick()` calls `injury.Heal(repairSpeedPerTick)` on every active (non-permanent) `Hediff_Injury`.

5. When `SummaryHealthPercent ≥ 0.99`, `OnRepairComplete()` fires: the player gets a positive message, `CurrentOccupant` is set to `null`, and `JobDriver_RepairAtStation`'s `tickAction` detects the change and ends the job cleanly.

---

## 🧱 Building Stats

| Stat | Value |
| --- | --- |
| Size | 2×2 tiles |
| Max HP | 300 |
| Work to Build | 4,000 ticks |
| Flammability | 50% |
| Power Draw | 250W |
| Cost | 150 Steel + 4 Industrial Components + 1 Spacer Component |
| Research | Mechanoid Repair Systems (Spacer, 1200pts) |
| Prerequisite Research | Mechanoid Basics (BioTech DLC) |

---

## 🔧 Building & Compiling

### Prerequisites

- .NET SDK 6+ (or Visual Studio 2022 / JetBrains Rider)
- RimWorld 1.6 installed via Steam

### Visual Studio / Rider

1. Open `Source/RobotRepairStation/RobotRepairStation.csproj`.
2. Set the `RimWorldPath` property in the `.csproj` (or as an environment variable) to your RimWorld install directory.
3. Build → Release. The DLL is automatically copied to `Assemblies/`.

### Command Line

```bash
cd Source/RobotRepairStation
dotnet build -c Release
```

### Default RimWorld Paths

| OS | Path |
| --- | --- |
| Windows | `C:\Program Files (x86)\Steam\steamapps\common\RimWorld` |
| Linux | `~/.steam/steam/steamapps/common/RimWorld` |
| macOS | `~/Library/Application Support/Steam/steamapps/common/RimWorld` |

---

## 🖼️ Adding the Texture

Place a **128 × 128 px** PNG at:

```text
Textures/Things/Buildings/RobotRepairStation.png
```

**Style guide:** Match the BioTech aesthetic — dark gunmetal panels with teal/blue accent lighting. The building is 2×2 tiles; keep the sprite visually centered with a subtle docking arm or cradle motif. If using `Graphic_Single`, the texture is not rotated, so design for a "top-down south-facing" perspective.

---

## 📦 Installation

1. Copy the `RobotRepairStation/` folder to your RimWorld mods directory:

    | OS | Mods Directory |
    | --- | --- |
    | Windows | `%APPDATA%\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Mods\` |
    | Linux | `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Mods/` |
    | macOS | `~/Library/Application Support/RimWorld/Mods/` |

2. Enable the mod in-game. Ensure **BioTech DLC** is active — the mod will not function without it.

3. Research *Mechanoid Basics*, then *Mechanoid Repair Systems* to unlock the building.

---

## 🌍 Localization

All player-visible strings live in `Languages/English/Keyed/RobotRepairStation.xml`. To add a translation:

1. Create `Languages/<LanguageName>/Keyed/RobotRepairStation.xml`
2. Copy the English file and replace the values (keep the keys identical)
3. RimWorld will fall back to English for any missing keys automatically

String keys use the `RRS_` prefix to avoid collisions. Keys containing `{0}` or `{1}` are format strings — pass arguments with `.Translate(arg0)`.

---

## ⚠️ Known Limitations & Design Decisions

- **No regeneration of lost body parts** — `HediffComp_GetsPermanent` injuries are intentionally skipped in `ApplyRepairTick()`. Regrowing limbs is out of scope for this building tier.
- **Single occupant per station** — The station supports exactly one mechanoid at a time. Place multiple stations for larger mechanoid squads.
- **Steel search radius is 8 cells** — Steel must be stockpiled near the station. The internal buffer (max 50 units) reduces search frequency.
- **ThinkTree patch targets `MechanoidConstant`** — If Ludeon restructures this tree in a future update, the XPath in `Patches/MechanoidThinkTree.xml` may need updating. Symptom: mechanoids never seek the station autonomously.
- **No Harmony patches** — All AI integration is done via XML ThinkTree patching, keeping compatibility risk low.

---

## 📜 License

MIT — free to use, modify, and distribute with attribution.

---

Built for RimWorld 1.6 · Requires BioTech DLC · Author: RexThar
