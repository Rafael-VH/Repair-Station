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
    /// Clase principal del edificio Robot Repair Station.
    /// Gestiona: registro en RepairStationTracker, aceptación/expulsión de mecanoides,
    /// consumo de acero con buffer interno, persistencia save/load, y UI (gizmos + inspector).
    /// </summary>
    public class Building_RobotRepairStation : Building
    {
        // ─── Estado serializable ──────────────────────────────────────────────

        /// <summary>Mecanoid actualmente en reparación. Serializado como referencia.</summary>
        private Pawn currentOccupant;

        /// <summary>Buffer interno de acero (unidades). Evita búsquedas en el mapa cada ciclo.</summary>
        private int steelBuffer = 0;

        private const int SteelBufferMax = 50;

        // ─── Cachés de comps ──────────────────────────────────────────────────

        private CompProperties_RobotRepairStation cachedCompProps;
        private CompPowerTrader cachedPowerComp;

        // ─── Propiedades públicas ─────────────────────────────────────────────

        public CompProperties_RobotRepairStation RepairProps =>
            cachedCompProps ?? (cachedCompProps = GetComp<CompRobotRepairStation>()?.Props);

        /// <summary>true si hay un mecanoid docked y está vivo.</summary>
        public bool IsOccupied => currentOccupant != null && !currentOccupant.Dead;

        /// <summary>true si el CompPowerTrader está alimentado.</summary>
        public bool HasPower => cachedPowerComp?.PowerOn ?? false;

        /// <summary>true si el buffer interno tiene al menos 1 unidad de acero.</summary>
        public bool HasSteel => steelBuffer > 0;

        /// <summary>El mecanoid docked, o null si la estación está libre.</summary>
        public Pawn CurrentOccupant => currentOccupant;

        // ═══════════════════════════════════════════════════════════════════════
        //  CICLO DE VIDA
        // ═══════════════════════════════════════════════════════════════════════

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            // Cachear comps una sola vez para evitar búsquedas lineales en cada tick.
            cachedPowerComp = GetComp<CompPowerTrader>();
            cachedCompProps = GetComp<CompRobotRepairStation>()?.Props;

            RepairStationTracker.GetOrCreate(map).Register(this);
        }

        /// <summary>
        /// Validación post-carga: si el ocupante guardado no tiene el job de reparación
        /// activo ni en cola, limpiar el estado para desbloquear la estación.
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
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  TICK
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Consume acero del buffer cada repairTickInterval ticks.
        /// CompRobotRepairStation.CompTick() aplica curación en el mismo tick,
        /// verificando HasSteel antes de curar para evitar ciclos gratuitos.
        /// </summary>
        protected override void Tick()
        {
            base.Tick();

            if (!HasPower)           return;
            if (!IsOccupied)         return;
            if (RepairProps == null) return;

            if (Find.TickManager.TicksGame % RepairProps.repairTickInterval == 0)
                TryConsumeSteel();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  GESTIÓN DE OCUPANTE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Acepta un mecanoid como ocupante. Llamado desde JobDriver_GoToRepairStation.
        /// </summary>
        public bool TryAcceptOccupant(Pawn mechanoid)
        {
            if (IsOccupied) return false;
            if (!HasPower)  return false;

            currentOccupant = mechanoid;
            return true;
        }

        /// <summary>
        /// Limpia currentOccupant cuando la reparación completa normalmente.
        /// El driver detecta el null en tickAction y termina el job.
        /// Solo llamado desde CompRobotRepairStation.OnRepairComplete.
        /// </summary>
        public void NotifyOccupantLeft()
        {
            currentOccupant = null;
        }

        /// <summary>
        /// Fuerza la salida del mecanoid. Escenarios: sin acero, sin energía,
        /// estación destruida, o gizmo manual del jugador.
        ///
        /// NO llama manualmente a reservationManager.Release porque EndCurrentJob
        /// ya limpia las reservas del driver internamente, evitando doble Release.
        /// </summary>
        public void EjectOccupant()
        {
            if (!IsOccupied) return;

            Pawn occupant   = currentOccupant;
            currentOccupant = null; // Limpiar antes de cualquier otra operación.

            if (occupant.CurJob?.def == RRS_JobDefOf.RRS_RepairAtStation)
                occupant.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  CONSUMO DE ACERO
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Camino rápido: descuenta del buffer sin búsqueda en el mapa.
        /// Si el buffer se vacía, busca acero en radio 8 celdas y recarga el buffer.
        /// Sin acero disponible: notifica al jugador y expulsa al ocupante.
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

            sb.Append($"Steel buffer: {steelBuffer}/{SteelBufferMax}");
            return sb.ToString().TrimEndNewlines();
        }
    }
}
