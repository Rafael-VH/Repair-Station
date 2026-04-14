using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using System.Linq;

namespace RobotRepairStation
{
    /// <summary>
    /// Edificio principal del Robot Repair Station.
    ///
    /// Gestiona el ciclo de vida del ocupante, el buffer de acero, la persistencia
    /// save/load y la UI (gizmos + inspector). La lógica de curación tick a tick
    /// reside en <see cref="CompRobotRepairStation"/> para mantener las
    /// responsabilidades separadas.
    ///
    /// Mejoras respecto a la versión inicial:
    /// - Gizmo de umbral de salud funcional (ajusta CompRobotRepairStation.repairThreshold).
    /// - Gizmo de prioridad por estación (permite al jugador preferir una estación sobre otra).
    /// - Notificación de "sin acero" convertida a carta persistente.
    /// - Efectos visuales al aceptar ocupante (polvo) y al expulsarlo.
    /// - InspectString mejorado con % de umbral activo y prioridad.
    /// </summary>
    public class Building_RobotRepairStation : Building
    {
        // ─── Estado serializable ──────────────────────────────────────────────

        /// <summary>Mecanoide actualmente en reparación. Serializado como referencia.</summary>
        private Pawn currentOccupant;

        /// <summary>
        /// Buffer interno de acero (unidades). Reduce las búsquedas en el mapa
        /// a una vez cada vez que el buffer se agota, en lugar de cada ciclo.
        /// </summary>
        private int steelBuffer = 0;

        /// <summary>
        /// Prioridad de esta estación (1 = más alta, valores mayores = más baja).
        /// Los mecanoides preferirán la estación con menor número de prioridad
        /// cuando varias sean accesibles. Serializado para persistir entre sesiones.
        /// </summary>
        private int stationPriority = 1;

        private const int SteelBufferMax = 50;
        private const int PriorityMin = 1;
        private const int PriorityMax = 9;

        // ─── Cachés de comps (inicializados en SpawnSetup) ───────────────────

        private CompProperties_RobotRepairStation cachedCompProps;
        private CompPowerTrader cachedPowerComp;
        private CompRobotRepairStation cachedComp;

        // ─── Propiedades públicas ─────────────────────────────────────────────

        /// <summary>
        /// Propiedades de configuración del comp de reparación.
        /// El valor se garantiza tras SpawnSetup; acceder antes puede devolver null.
        /// </summary>
        public CompProperties_RobotRepairStation RepairProps => cachedCompProps;

        /// <summary>Devuelve <c>true</c> si hay un mecanoide docked y está vivo.</summary>
        public bool IsOccupied => currentOccupant != null && !currentOccupant.Dead;

        /// <summary>Devuelve <c>true</c> si el CompPowerTrader está alimentado.</summary>
        public bool HasPower => cachedPowerComp?.PowerOn ?? false;

        /// <summary>Devuelve <c>true</c> si el buffer interno tiene al menos 1 unidad de acero.</summary>
        public bool HasSteel => steelBuffer > 0;

        /// <summary>El mecanoide docked, o <c>null</c> si la estación está libre.</summary>
        public Pawn CurrentOccupant => currentOccupant;

        /// <summary>
        /// Prioridad de esta estación para la búsqueda de mecanoides.
        /// Valor más bajo = mayor prioridad. Rango: 1–9.
        /// </summary>
        public int StationPriority => stationPriority;

        /// <summary>
        /// Umbral de salud activo para esta instancia.
        /// Lee el valor del comp serializado (ajustable por el jugador).
        /// Fallback al valor de props si el comp no está disponible aún.
        /// </summary>
        public float ActiveRepairThreshold =>
            cachedComp?.repairThreshold ?? cachedCompProps?.repairHealthThreshold ?? 0.5f;

        // ═══════════════════════════════════════════════════════════════════════
        //  CICLO DE VIDA
        // ═══════════════════════════════════════════════════════════════════════

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            // Cachear comps una sola vez para evitar búsquedas lineales en cada tick.
            cachedPowerComp = GetComp<CompPowerTrader>();
            cachedComp = GetComp<CompRobotRepairStation>();
            cachedCompProps = cachedComp?.Props;

            RepairStationTracker.GetOrCreate(map).Register(this);
        }

        /// <summary>
        /// Validación post-carga: si el ocupante guardado no tiene el job de reparación
        /// activo ni en cola, se limpia el estado para desbloquear la estación.
        /// </summary>
        public override void PostMapInit()
        {
            base.PostMapInit();

            if (currentOccupant == null) return;

            bool hasActiveJob =
                currentOccupant.CurJob?.def == RRS_JobDefOf.RRS_RepairAtStation ||
                currentOccupant.CurJob?.def == RRS_JobDefOf.RRS_GoToRepairStation ||
                (currentOccupant.jobs?.jobQueue?.Any(
                    qj => qj.job?.def == RRS_JobDefOf.RRS_RepairAtStation ||
                          qj.job?.def == RRS_JobDefOf.RRS_GoToRepairStation) ?? false);

            if (!hasActiveJob)
            {
                Log.Warning(
                    $"[RobotRepairStation] {currentOccupant.LabelShort} estaba registrado en" +
                    $" {Label} pero no tiene el job de reparación activo ni en cola. Limpiando estado.");
                currentOccupant = null;
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            // Map puede ser null en estados intermedios (minificación, caravanas).
            if (Map != null)
                RepairStationTracker.GetOrCreate(Map).Deregister(this);

            EjectOccupant();
            base.DeSpawn(mode);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SERIALIZACIÓN
        // ═══════════════════════════════════════════════════════════════════════

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref currentOccupant, "currentOccupant");
            Scribe_Values.Look(ref steelBuffer, "steelBuffer", 0);
            Scribe_Values.Look(ref stationPriority, "stationPriority", 1);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  TICK
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Consume acero del buffer cada <c>repairTickInterval</c> ticks.
        ///
        /// Se aplica un offset derivado del ID único del edificio (<c>thingIDNumber.HashOffset()</c>)
        /// para distribuir los ticks de múltiples estaciones a lo largo del frame y evitar
        /// picos de CPU cuando varias instancias coinciden en el mismo tick exacto.
        /// </summary>
        protected override void Tick()
        {
            base.Tick();

            if (!HasPower) return;
            if (!IsOccupied) return;
            if (RepairProps == null) return;

            if ((Find.TickManager.TicksGame + thingIDNumber.HashOffset()) % RepairProps.repairTickInterval == 0)
                TryConsumeSteel();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  GESTIÓN DE OCUPANTE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Acepta un mecanoide como ocupante. Llamado desde
        /// <see cref="JobDriver_GoToRepairStation"/> al llegar a la celda de interacción.
        /// </summary>
        /// <returns><c>true</c> si el mecanoide fue aceptado correctamente.</returns>
        public bool TryAcceptOccupant(Pawn mechanoid)
        {
            if (IsOccupied) return false;
            if (!HasPower) return false;

            currentOccupant = mechanoid;

            if (Spawned)
            {
                FleckMaker.ThrowDustPuff(DrawPos, Map, 1.5f);
                SoundDefOf.TinyBell.PlayOneShot(SoundInfo.InMap(new TargetInfo(Position, Map)));
            }

            return true;
        }

        /// <summary>
        /// Limpia <c>currentOccupant</c> cuando la reparación se completa normalmente.
        /// Solo debe llamarse desde <see cref="CompRobotRepairStation"/>.
        /// </summary>
        public void NotifyOccupantLeft()
        {
            currentOccupant = null;
        }

        /// <summary>
        /// Fuerza la salida del mecanoide docked. Se usa cuando la estación pierde
        /// energía, se queda sin acero, es destruida, o el jugador lo solicita
        /// manualmente mediante el gizmo de expulsión.
        /// </summary>
        public void EjectOccupant()
        {
            if (!IsOccupied) return;

            Pawn occupant = currentOccupant;
            currentOccupant = null;

            if (occupant.CurJob?.def == RRS_JobDefOf.RRS_RepairAtStation)
                occupant.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  CONSUMO DE ACERO
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Descuenta acero del buffer interno. Si el buffer se vacía, busca acero
        /// en un radio de 8 celdas y recarga hasta <see cref="SteelBufferMax"/> unidades.
        /// Si no se encuentra acero, notifica al jugador con una carta persistente y
        /// expulsa al ocupante.
        /// </summary>
        private void TryConsumeSteel()
        {
            int toConsume = RepairProps?.steelPerRepairCycle ?? 1;

            if (steelBuffer >= toConsume)
            {
                steelBuffer -= toConsume;
                return;
            }

            TraverseParms traverseParams = currentOccupant != null
                ? TraverseParms.For(currentOccupant, Danger.Deadly)
                : TraverseParms.For(TraverseMode.NoPassClosedDoors);

            Thing steel = GenClosest.ClosestThingReachable(
                Position,
                Map,
                ThingRequest.ForDef(ThingDefOf.Steel),
                PathEndMode.ClosestTouch,
                traverseParams,
                maxDistance: 8f
            );

            if (steel != null)
            {
                int take = Mathf.Min(steel.stackCount, SteelBufferMax);

                steel.stackCount -= take;
                if (steel.stackCount <= 0)
                    steel.Destroy(DestroyMode.Vanish);

                steelBuffer = Mathf.Max(0, take - toConsume);
            }
            else
            {
                // Carta persistente en lugar de mensaje efímero.
                Find.LetterStack.ReceiveLetter(
                    "RRS_LetterNoSteelLabel".Translate(),
                    "RRS_LetterNoSteelText".Translate(currentOccupant?.LabelShort ?? "mechanoid"),
                    LetterDefOf.NegativeEvent,
                    this
                );
                EjectOccupant();
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  UI — GIZMOS
        // ═══════════════════════════════════════════════════════════════════════

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;

            // ── Gizmo: ajustar umbral de salud ───────────────────────────────
            // Permite al jugador cambiar el % de salud a partir del cual los
            // mecanoides buscan esta estación. El valor se persiste en
            // CompRobotRepairStation.repairThreshold (serializado).
            yield return new Command_Action
            {
                defaultLabel = "RRS_GizmoSetThreshold".Translate() +
                               $": {ActiveRepairThreshold.ToStringPercent("F0")}",
                defaultDesc = "RRS_GizmoSetThresholdDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/SetTargetFuelLevel", false)
                               ?? BaseContent.BadTex,
                action = () =>
                {
                    if (cachedComp == null) return;

                    // Dialog_Slider(Func<int,string> textGetter, int min, int max, Action<int> setter, int initial)
                    // Constructor posicional en RimWorld 1.6 — sin nombres de parámetro.
                    int initialPct = Mathf.RoundToInt(ActiveRepairThreshold * 100f);
                    Find.WindowStack.Add(new Dialog_Slider(
                        x => "RRS_GizmoSetThreshold".Translate() + $": {(x / 100f).ToStringPercent("F0")}",
                        1,
                        100,
                        val => cachedComp.repairThreshold = val / 100f,
                        initialPct
                    ));
                }
            };

            // ── Gizmo: prioridad de la estación ──────────────────────────────
            // Valor 1 = mayor prioridad. Los mecanoides prefieren la estación con
            // menor número cuando varias son accesibles. Cicla entre 1 y PriorityMax.
            yield return new Command_Action
            {
                defaultLabel = "RRS_GizmoPriority".Translate() + $": {stationPriority}",
                defaultDesc = "RRS_GizmoPriorityDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/ReorderUp", false)
                               ?? BaseContent.BadTex,
                action = () =>
                {
                    // Cíclico: 1 → 2 → … → PriorityMax → 1
                    stationPriority = (stationPriority % PriorityMax) + 1;
                }
            };

            // ── Gizmo: expulsar ocupante ─────────────────────────────────────
            if (IsOccupied)
            {
                yield return new Command_Action
                {
                    defaultLabel = "RRS_GizmoEjectOccupant".Translate(),
                    defaultDesc = "RRS_GizmoEjectOccupantDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport", false)
                                   ?? BaseContent.BadTex,
                    action = EjectOccupant
                };
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  UI — INSPECTOR
        // ═══════════════════════════════════════════════════════════════════════

        public override string GetInspectString()
        {
            var sb = new StringBuilder();

            string baseStr = base.GetInspectString();
            if (!baseStr.NullOrEmpty())
                sb.AppendLine(baseStr);

            if (!HasPower)
            {
                sb.AppendLine("RRS_InspectorNoPower".Translate());
            }
            else if (IsOccupied)
            {
                float healthPct = currentOccupant.health.summaryHealth.SummaryHealthPercent;
                sb.AppendLine("RRS_InspectorCurrentOccupant".Translate(currentOccupant.LabelShort));
                sb.AppendLine($"Health: {healthPct * 100f:F0}%");

                // Tiempo estimado de reparación restante.
                float healthToRecover = 1f - healthPct;
                if (healthToRecover > 0f && RepairProps != null && RepairProps.repairSpeedPerTick > 0f)
                {
                    // HP recuperados por tick efectivo = repairSpeedPerTick (por lesión).
                    // Estimación conservadora: asume 1 lesión activa promedio.
                    float ticksLeft = (healthToRecover / RepairProps.repairSpeedPerTick) * RepairProps.repairTickInterval;
                    sb.AppendLine("RRS_InspectorETA".Translate(((int)ticksLeft).ToStringTicksToPeriod()));
                }

                if (!HasSteel)
                    sb.AppendLine("RRS_InspectorNoSteel".Translate());
            }
            else
            {
                sb.AppendLine("RRS_InspectorEmpty".Translate());
            }

            sb.AppendLine($"Steel buffer: {steelBuffer}/{SteelBufferMax}");
            sb.AppendLine("RRS_InspectorThreshold".Translate(ActiveRepairThreshold.ToStringPercent("F0")));
            sb.Append("RRS_InspectorPriority".Translate(stationPriority));

            return sb.ToString().TrimEndNewlines();
        }
    }
}
