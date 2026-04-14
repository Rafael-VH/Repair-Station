using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using System.Linq;

namespace RobotRepairStation
{
    /// <summary>
    /// Edificio principal del Robot Repair Station.
    ///
    /// Gestiona el ciclo de vida del ocupante, el consumo de acero vía
    /// <see cref="CompRefuelable"/>, la persistencia save/load y la UI
    /// (gizmos + inspector).
    ///
    /// El acero ya no se busca manualmente en el mapa: RimWorld gestiona
    /// automáticamente el reabastecimiento mediante trabajos de transporte
    /// asignados a colonos, igual que el generador de combustible.
    ///
    /// La lógica de curación tick a tick reside en
    /// <see cref="CompRobotRepairStation"/> para mantener las
    /// responsabilidades separadas.
    /// </summary>
    public class Building_RobotRepairStation : Building
    {
        // ─── Estado serializable ──────────────────────────────────────────────

        /// <summary>Mecanoide actualmente en reparación. Serializado como referencia.</summary>
        private Pawn currentOccupant;

        // NOTA: steelBuffer eliminado — CompRefuelable es ahora la única fuente
        // de verdad sobre el nivel de acero disponible.

        // ─── Cachés de comps (inicializados en SpawnSetup) ───────────────────

        private CompProperties_RobotRepairStation cachedCompProps;
        private CompPowerTrader   cachedPowerComp;
        private CompRefuelable    cachedRefuelComp;   // ← nuevo

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

        /// <summary>
        /// Devuelve <c>true</c> si el depósito de acero contiene al menos
        /// 1 unidad disponible para el próximo ciclo de reparación.
        /// Delegado en CompRefuelable para consistencia con la UI.
        /// </summary>
        public bool HasSteel => (cachedRefuelComp?.Fuel ?? 0f) >= 1f;

        /// <summary>El mecanoide docked, o <c>null</c> si la estación está libre.</summary>
        public Pawn CurrentOccupant => currentOccupant;

        // ═══════════════════════════════════════════════════════════════════════
        //  CICLO DE VIDA
        // ═══════════════════════════════════════════════════════════════════════

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            // Cachear comps una sola vez para evitar búsquedas lineales en cada tick.
            cachedPowerComp  = GetComp<CompPowerTrader>();
            cachedRefuelComp = GetComp<CompRefuelable>();      // ← nuevo
            cachedCompProps  = GetComp<CompRobotRepairStation>()?.Props;

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
                currentOccupant.CurJob?.def == RRS_JobDefOf.RRS_RepairAtStation   ||
                currentOccupant.CurJob?.def == RRS_JobDefOf.RRS_GoToRepairStation  ||
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
            // steelBuffer eliminado: CompRefuelable serializa su propio estado.
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  TICK
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Consume acero del depósito (CompRefuelable) cada
        /// <c>repairTickInterval</c> ticks mientras haya un mecanoide docked.
        ///
        /// El offset derivado de <c>thingIDNumber.HashOffset()</c> distribuye los
        /// ticks de múltiples estaciones a lo largo del frame para evitar picos de CPU.
        ///
        /// <see cref="CompRobotRepairStation.CompTick"/> aplica la curación en el
        /// mismo intervalo y verifica <see cref="HasSteel"/> antes de curar.
        /// </summary>
        protected override void Tick()
        {
            base.Tick();

            if (!HasPower)           return;
            if (!IsOccupied)         return;
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
            if (!HasPower)  return false;

            currentOccupant = mechanoid;
            return true;
        }

        /// <summary>
        /// Limpia <c>currentOccupant</c> cuando la reparación se completa normalmente.
        /// El driver detecta el <c>null</c> en su <c>tickAction</c> y termina el job.
        /// Solo debe llamarse desde <see cref="CompRobotRepairStation"/>.
        /// </summary>
        public void NotifyOccupantLeft()
        {
            currentOccupant = null;
        }

        /// <summary>
        /// Fuerza la salida del mecanoide docked. Se usa cuando la estación pierde
        /// energía, se queda sin acero, es destruida, o el jugador lo solicita
        /// mediante el gizmo de expulsión.
        /// </summary>
        public void EjectOccupant()
        {
            if (!IsOccupied) return;

            Pawn occupant   = currentOccupant;
            currentOccupant = null;

            if (occupant.CurJob?.def == RRS_JobDefOf.RRS_RepairAtStation)
                occupant.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  CONSUMO DE ACERO  (ahora delegado en CompRefuelable)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Descuenta <see cref="CompProperties_RobotRepairStation.steelPerRepairCycle"/>
        /// unidades del depósito de acero gestionado por <see cref="CompRefuelable"/>.
        ///
        /// Si el depósito no tiene suficiente acero:
        ///   - Se notifica al jugador con un mensaje negativo.
        ///   - Se expulsa al mecanoide docked.
        ///
        /// El reabastecimiento posterior es automático: RimWorld asignará un trabajo
        /// de transporte a un colono en cuanto el nivel baje del umbral configurado
        /// en <c>autoRefuelPercent</c>, igual que el generador de combustible.
        /// </summary>
        private void TryConsumeSteel()
        {
            int toConsume = RepairProps?.steelPerRepairCycle ?? 1;

            if (cachedRefuelComp == null)
            {
                Log.Error("[RobotRepairStation] CompRefuelable no encontrado en la estación. Verifica el ThingDef.");
                return;
            }

            if (cachedRefuelComp.Fuel >= toConsume)
            {
                cachedRefuelComp.ConsumeFuel(toConsume);
            }
            else
            {
                // Sin acero suficiente: notificar y expulsar.
                Messages.Message(
                    "RRS_LetterNoSteelText".Translate(currentOccupant?.LabelShort ?? "mechanoid"),
                    this,
                    MessageTypeDefOf.NegativeEvent
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
            // NOTA: CompRefuelable añade automáticamente el gizmo de nivel de acero
            // y el botón de reabastecimiento. No se necesita código extra aquí.

            if (IsOccupied)
            {
                yield return new Command_Action
                {
                    defaultLabel = "RRS_GizmoEjectOccupant".Translate(),
                    defaultDesc  = "RRS_GizmoEjectOccupantDesc".Translate(),
                    icon         = ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport"),
                    action       = EjectOccupant
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
                sb.AppendLine("RRS_InspectorCurrentOccupant".Translate(currentOccupant.LabelShort));
                sb.AppendLine($"Health: {currentOccupant.health.summaryHealth.SummaryHealthPercent * 100f:F0}%");
                if (!HasSteel)
                    sb.AppendLine("RRS_InspectorNoSteel".Translate());
            }
            else
            {
                sb.AppendLine("RRS_InspectorEmpty".Translate());
            }

            // El nivel de acero detallado ya lo muestra CompRefuelable en su propio
            // bloque del inspector ("Steel: X / 50"), así que no se duplica aquí.

            return sb.ToString().TrimEndNewlines();
        }
    }
}
