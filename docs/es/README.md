# 🤖 Estación de Reparación de Robots

## Un Mod de RimWorld BioTech

![RimWorld](https://img.shields.io/badge/RimWorld-1.6-8B4513?style=flat-square)
![BioTech DLC](https://img.shields.io/badge/BioTech_DLC-Requerido-6A0DAD?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-4.8-239120?style=flat-square&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-10.0-239120?style=flat-square&logo=csharp)
![Licencia](https://img.shields.io/badge/Licencia-MIT-blue?style=flat-square)

**Estación de acoplamiento de reparación automática para mecanoides de BioTech.**
Cuando están dañados, tus mecanoides buscarán la estación de forma autónoma y se repararán solos — sin necesidad de microgestión.

> 📖 **English documentation** available at [`../../README.md`](../../README.md)
> 📖 **Documentação em português** disponible en [`../pt/README.md`](../pt/README.md)

---

## ✨ Características

- **Reparación autónoma** — Los mecanoides detectan cuándo su salud cae por debajo de un umbral configurable y navegan a la estación disponible más cercana sin intervención del jugador
- **Consumo de recursos** — La reparación consume acero de los almacenes cercanos, gestionado mediante un buffer interno (hasta 50 unidades) para reducir las búsquedas en el mapa por ciclo
- **Requiere energía** — Necesita una conexión eléctrica activa; la estación se apaga limpiamente cuando se pierde el suministro
- **Expulsión manual** — Un botón de gizmo permite al jugador retirar forzosamente un mecanoide a mitad de reparación
- **Totalmente configurable** — Todos los parámetros (umbral de salud, velocidad de reparación, coste de acero, rango de detección) son editables en el XML sin necesidad de recompilar
- **Bloqueado por investigación** — Desbloqueado por *Sistemas de Reparación de Mecanoides* (nivel Espacial, 1200 pts), requiere *Conceptos Básicos de Mecanoides* primero
- **Se puede averiar** — Requiere mantenimiento periódico, coherente con los edificios industriales del juego base
- **Seguro para guardar/cargar** — Todo el estado (ocupante, buffer de acero) se serializa con `Scribe_References` y `Scribe_Values`
- **Sin pérdida de partes del cuerpo** — Solo repara lesiones activas; el daño permanente no se restaura (por diseño)
- **Sin dependencia de Harmony** — Toda la integración de IA se realiza mediante parches XML al ThinkTree, manteniendo el riesgo de compatibilidad bajo

---

## 🏗️ Descripción de la Arquitectura

El mod se construye alrededor de cuatro sistemas interconectados:

```text
┌──────────────────────────────────────────────────────────────────┐
│                    CAPA DE IA (ThinkTree)                        │
│                                                                  │
│  ThinkNode_ConditionalNeedsRepair                                │
│    └─ Comprueba: ¿es mecanoide? ¿del jugador? ¿salud<umbral?     │
│       └─ JobGiver_GoToRepairStation                              │
│            └─ Emite: job RRS_GoToRepairStation                   │
└──────────────────────────────┬───────────────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────────────┐
│                       CAPA DE JOBS                               │
│                                                                  │
│  JobDriver_GoToRepairStation                                     │
│    1. GotoThing → caminar hasta InteractionCell                  │
│    2. dock (Instant) → TryAcceptOccupant → encolar job reparo    │
│                                                                  │
│  JobDriver_RepairAtStation                                       │
│    - Espera (ToilCompleteMode.Never)                             │
│    - Termina cuando CurrentOccupant pasa a null                  │
└──────────────────────────────┬───────────────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────────────┐
│                    CAPA DEL EDIFICIO                             │
│                                                                  │
│  Building_RobotRepairStation                                     │
│    - Gestiona ocupante (TryAcceptOccupant / EjectOccupant)       │
│    - Tick: TryConsumeSteel cada repairTickInterval               │
│    - Buffer de acero (hasta 50 uds) evita búsquedas por tick     │
│    - Gizmos, InspectString, guardar/cargar                       │
│                                                                  │
│  CompRobotRepairStation (ThingComp)                              │
│    - CompTick: ApplyRepairTick cada repairTickInterval           │
│    - Cura todas las instancias Hediff_Injury activas             │
│    - Llama a OnRepairComplete cuando salud ≥ 99%                 │
└──────────────────────────────┬───────────────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────────────┐
│                    CAPA DE REGISTRO                              │
│                                                                  │
│  RepairStationTracker (MapComponent)                             │
│    - Registro/baja en O(1) en SpawnSetup / DeSpawn               │
│    - Los ThinkNodes iteran esta lista en lugar de buscar el mapa │
│    - Declarado en MapComponentDefs.xml; instanciado por RW       │
└──────────────────────────────────────────────────────────────────┘
```

---

## 📁 Estructura de Carpetas

```text
RobotRepairStation/
│
├── About/
│   └── About.xml                        ← Metadatos del mod, packageId, dependencia BioTech
│
├── Assemblies/
│   └── RobotRepairStation.dll           ← Salida compilada (no editar manualmente)
│
├── 1.6/
│   ├── Defs/
│   │   ├── JobDefs/
│   │   │   └── JobDefs_RobotRepair.xml      ← RRS_GoToRepairStation + RRS_RepairAtStation
│   │   ├── MapComponentDefs/
│   │   │   └── MapComponentDefs.xml         ← Registra RepairStationTracker en RimWorld
│   │   ├── ResearchProjectDefs/
│   │   │   └── ResearchDefs.xml             ← "Sistemas de Reparación de Mecanoides" (Espacial, 1200 pts)
│   │   └── ThingDefs/
│   │       └── Buildings_RobotRepairStation.xml  ← ThingDef: tamaño, coste, comps, investigación
│   │
│   ├── Languages/
│   │   ├── English/Keyed/
│   │   │   └── RobotRepairStation.xml       ← Cadenas visibles para el jugador (locale base)
│   │   ├── Spanish/Keyed/
│   │   │   └── RobotRepairStation.xml       ← Traducción al español (España)
│   │   ├── SpanishLatin/Keyed/
│   │   │   └── RobotRepairStation.xml       ← Traducción al español (Latinoamérica)
│   │   └── Portuguese/Keyed/
│   │       └── RobotRepairStation.xml       ← Traducción al portugués
│   │
│   └── Patches/
│       └── MechanoidThinkTree.xml           ← Inyecta nodo de reparación en MechanoidConstant
│
├── Source/
│   ├── RRS_Mod.cs                           ← Bootstrap StaticConstructorOnStartup
│   ├── RRS_JobDefOf.cs                      ← Referencias estáticas de jobs [DefOf]
│   ├── Building_RobotRepairStation.cs       ← Edificio principal: ocupante, acero, UI
│   ├── CompProperties_RobotRepairStation.cs ← CompProperties + CompRobotRepairStation (tick de curación)
│   ├── JobDriver_GoToRepairStation.cs       ← Driver del job caminar-hacia-estación
│   ├── JobDriver_RepairAtStation.cs         ← Driver del job reparación acoplada
│   ├── ThinkNode_ConditionalNeedsRepair.cs  ← Condicional IA + JobGiver + RepairStationUtility
│   └── RepairStationTracker.cs              ← Registro de estaciones como MapComponent
│
├── Textures/
│   └── Things/
│       └── Buildings/
│           └── RobotRepairStation.png       ← Sprite del edificio 128×128 (debe añadirse)
│
├── docs/
│   └── es/
│       └── README.md                        ← Este archivo — documentación en español
│
└── .vscode/
    ├── mod.csproj                           ← Archivo de proyecto (net480, x64)
    ├── tasks.json                           ← Tareas de compilación (Windows + Linux)
    ├── launch.json                          ← Configuraciones de lanzamiento y depurador
    └── extensions.json                      ← Extensiones recomendadas para VS Code
```

---

## ⚙️ Referencia de Configuración

Todos los parámetros son ajustables directamente en `1.6/Defs/ThingDefs/Buildings_RobotRepairStation.xml` dentro del bloque `<li Class="RobotRepairStation.CompProperties_RobotRepairStation">` — sin necesidad de recompilar.

| Propiedad | Por defecto | Descripción |
| --- | --- | --- |
| `repairHealthThreshold` | `0.5` | Fracción de salud (0–1) por debajo de la cual el mecanoide busca reparación. `0.5` = 50%. |
| `repairSpeedPerTick` | `0.0005` | HP restaurados por tick de juego en cada lesión activa. |
| `steelPerRepairCycle` | `1` | Unidades de acero consumidas por intervalo de reparación. Con los valores por defecto, ~7,2 unidades/hora. |
| `repairTickInterval` | `500` | Ticks entre cada ciclo de consumo de acero y curación (~8,3 s a velocidad ×1). Controla tanto la granularidad de recursos como el coste de CPU. |
| `maxRepairRange` | `30` | Distancia máxima en celdas para que un mecanoide detecte esta estación y se desplace a ella. |

> **Consejo de ajuste:** `repairSpeedPerTick` y `repairTickInterval` están acoplados. Los HP efectivos curados por segundo son simplemente `repairSpeedPerTick × 60`.

---

## 🔬 Cómo Funciona la Reparación (Paso a Paso)

1. En cada tick de IA, `ThinkNode_ConditionalNeedsRepair.Satisfied()` evalúa cada mecanoide del jugador en este orden (las comprobaciones más baratas primero):
   - ¿Es un mecanoide? ¿Es del jugador?
   - ¿Está ya ejecutando un job de reparación (`RRS_RepairAtStation` o `RRS_GoToRepairStation`)?
   - ¿Hay una estación alimentada, libre y alcanzable dentro de `maxRepairRange`? *(la más costosa — se ejecuta al final)*
   - ¿La salud está por debajo de `repairHealthThreshold`?

2. Si todas las condiciones se cumplen, `JobGiver_GoToRepairStation` emite un job `RRS_GoToRepairStation` apuntando a la estación válida más cercana, verificando antes que ningún otro pawn de la misma facción la tenga ya reservada.

3. `JobDriver_GoToRepairStation` lleva al mecanoide hasta la `InteractionCell` de la estación, después llama a `Building_RobotRepairStation.TryAcceptOccupant()` y encola `RRS_RepairAtStation`. Si una condición de carrera rellena la estación entre el desplazamiento y el acoplamiento, el job termina como `Incompletable` y el mecanoide lo reintentará.

4. Cada `repairTickInterval` ticks mientras está acoplado:
   - **Tick del edificio:** `TryConsumeSteel()` descuenta del buffer interno. Si el buffer está vacío, busca acero en un radio de 8 celdas y recarga hasta 50 unidades. Si no se encuentra, el mecanoide es expulsado y se notifica al jugador.
   - **Tick del comp:** `ApplyRepairTick()` llama a `injury.Heal(repairSpeedPerTick)` sobre cada `Hediff_Injury` activa (no permanente). La curación se omite si el buffer de acero está vacío tras el tick del edificio.

5. Cuando `SummaryHealthPercent ≥ 0.99`, se activa `OnRepairComplete()`: el jugador recibe un mensaje positivo, `CurrentOccupant` se pone a `null`, y el `tickAction` de `JobDriver_RepairAtStation` detecta el cambio y termina el job limpiamente.

6. Al cargar, `PostMapInit()` valida que el ocupante serializado sigue teniendo un job de reparación activo o en cola. Si no es así (p. ej. guardado editado externamente), la referencia al ocupante se limpia y se registra una advertencia, evitando que la estación quede bloqueada permanentemente.

---

## 🧱 Estadísticas del Edificio

| Estadística | Valor |
| --- | --- |
| Tamaño | 2×2 celdas |
| HP máximos | 300 |
| Trabajo para construir | 4.000 ticks |
| Inflamabilidad | 50% |
| Belleza | −2 |
| Consumo eléctrico | 250W |
| Coste | 150 Acero + 4 Componentes Industriales + 1 Componente Espacial |
| Investigación | Sistemas de Reparación de Mecanoides (Espacial, 1200 pts) |
| Investigación previa requerida | Conceptos Básicos de Mecanoides (DLC BioTech) |
| Radio de búsqueda de acero | 8 celdas |
| Capacidad del buffer de acero | 50 unidades |

---

## 🔧 Compilación

### Requisitos Previos

- .NET SDK para `net480` (Visual Studio 2022 / JetBrains Rider o CLI de `dotnet`)
- RimWorld 1.6 instalado vía Steam
- Paquete NuGet `Krafs.Rimworld.Ref` (se resuelve automáticamente al compilar)

### Visual Studio / Rider

1. Abre `.vscode/mod.csproj`.
2. Compilar → Release. El DLL se genera automáticamente en `1.6/Assemblies/` como `RepairStation.dll`.

### Línea de Comandos

```bash
cd .vscode
dotnet build -c Release
```

### VS Code

Usa la tarea **Build & Run** (`Ctrl+Shift+B`) definida en `.vscode/tasks.json`. Una configuración de lanzamiento separada **Attach Debugger** conecta Mono en el puerto `56000` para depuración en vivo.

### Rutas Predeterminadas de RimWorld

| SO | Ruta |
| --- | --- |
| Windows | `C:\Program Files (x86)\Steam\steamapps\common\RimWorld` |
| Linux | `~/.steam/steam/steamapps/common/RimWorld` |
| macOS | `~/Library/Application Support/Steam/steamapps/common/RimWorld` |

---

## 🖼️ Añadir la Textura

Coloca un PNG de **128 × 128 px** en:

```text
Textures/Things/Buildings/RobotRepairStation.png
```

**Guía de estilo:** Sigue la estética de BioTech — paneles de acero oscuro con iluminación de acento en azul/verde azulado. El edificio ocupa 2×2 celdas; mantén el sprite visualmente centrado con un motivo sutil de brazo o cuna de acoplamiento. El ThingDef usa `Graphic_Single`, así que la textura no rota — diseña para una perspectiva cenital orientada al sur.

---

## 📦 Instalación

1. Copia la carpeta `RobotRepairStation/` al directorio de mods de RimWorld:

    | SO | Directorio de Mods |
    | --- | --- |
    | Windows | `%APPDATA%\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Mods\` |
    | Linux | `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Mods/` |
    | macOS | `~/Library/Application Support/RimWorld/Mods/` |

2. Activa el mod en el juego. Asegúrate de que el **DLC BioTech** está activo.

3. Investiga *Conceptos Básicos de Mecanoides* y luego *Sistemas de Reparación de Mecanoides* para desbloquear el edificio.

---

## 🌍 Localización

Todas las cadenas visibles para el jugador se encuentran en `1.6/Languages/<Idioma>/Keyed/RobotRepairStation.xml`. Idiomas incluidos actualmente:

| Idioma | Ruta |
| --- | --- |
| Inglés | `1.6/Languages/English/Keyed/` |
| Español (España) | `1.6/Languages/Spanish/Keyed/` |
| Español (Latinoamérica) | `1.6/Languages/SpanishLatin/Keyed/` |
| Portugués | `1.6/Languages/Portuguese/Keyed/` |

Para añadir una nueva traducción:

1. Crea `1.6/Languages/<NombreIdioma>/Keyed/RobotRepairStation.xml`
2. Copia el archivo en inglés y reemplaza los valores (mantén las claves idénticas)
3. RimWorld usa el inglés automáticamente como respaldo para cualquier clave faltante

Todas las claves usan el prefijo `RRS_` para evitar colisiones con otros mods. Las claves que contienen `{0}` son cadenas de formato — se rellenan en tiempo de ejecución con valores como la etiqueta corta del mecanoide.

---

## ⚠️ Limitaciones Conocidas y Decisiones de Diseño

- **Sin regeneración de partes del cuerpo perdidas** — Las lesiones de tipo `HediffComp_GetsPermanent` se omiten intencionalmente en `ApplyRepairTick()`. La regeneración de miembros está fuera del alcance de este nivel de edificio.
- **Un solo ocupante por estación** — La estación admite exactamente un mecanoide a la vez. Coloca múltiples estaciones para escuadrones de mecanoides grandes.
- **El radio de búsqueda de acero es de 8 celdas** — El acero debe estar almacenado cerca de la estación. El buffer interno (máx. 50 unidades) reduce la frecuencia de búsqueda.
- **El parche del ThinkTree apunta a `MechanoidConstant`** — Si Ludeon reestructura este árbol en una actualización futura, el XPath en `1.6/Patches/MechanoidThinkTree.xml` puede necesitar actualización. Síntoma: los mecanoides nunca buscan la estación de forma autónoma.
- **Sin parches de Harmony** — Toda la integración de IA se realiza mediante parches XML al ThinkTree, manteniendo el riesgo de compatibilidad bajo.
- **El nombre del ensamblado es `RepairStation`** — El `RootNamespace` y `AssemblyName` en el `.csproj` están definidos como `RepairStation`, mientras que el namespace de C# en todo el código fuente es `RobotRepairStation`. Ten en cuenta esta distinción si renombras alguno de los dos.

---

## 📜 Licencia

MIT — libre de usar, modificar y distribuir con atribución.

---

Construido para RimWorld 1.6 · Requiere el DLC BioTech · Autor: RexThar
