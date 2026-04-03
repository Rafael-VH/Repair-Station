using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RobotRepairStation
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  COMP PROPERTIES
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Propiedades configurables del componente de reparación.
    /// Se leen desde el XML del ThingDef (bloque CompProperties_RobotRepairStation).
    /// Todos los campos tienen valores por defecto para compatibilidad.
    /// </summary>
    public class CompProperties_RobotRepairStation : CompProperties
    {
        /// <summary>Fracción de salud (0–1) bajo la que el mecanoid busca reparación. Por defecto: 0.5</summary>
        public float repairHealthThreshold = 0.5f;

        /// <summary>HP curados por tick por cada lesión activa. Por defecto: 0.0005</summary>
        public float repairSpeedPerTick = 0.0005f;

        /// <summary>Unidades de acero consumidas por ciclo de reparación. Por defecto: 1</summary>
        public int steelPerRepairCycle = 1;

        /// <summary>Ticks entre cada ciclo de curación y consumo de acero. Por defecto: 500 (~8.3s a ×1)</summary>
        public int repairTickInterval = 500;

        /// <summary>Distancia máxima en celdas para detectar la estación. Por defecto: 30</summary>
        public float maxRepairRange = 30f;

        public CompProperties_RobotRepairStation()
        {
            compClass = typeof(CompRobotRepairStation);
        }
    }


    // ═══════════════════════════════════════════════════════════════════════════
    //  COMP — LÓGICA DE CURACIÓN TICK A TICK
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Componente adjunto a Building_RobotRepairStation.
    /// Aplica curación a las lesiones activas del mecanoid docked cada repairTickInterval ticks.
    /// </summary>
    public class CompRobotRepairStation : ThingComp
    {
        public CompProperties_RobotRepairStation Props =>
            (CompProperties_RobotRepairStation)props;

        private Building_RobotRepairStation Station =>
            (Building_RobotRepairStation)parent;

        /// <summary>
        /// Llamado cada tick por RimWorld después del Tick() del edificio padre.
        /// Sale rápido si no hay condiciones para curar, minimizando coste de CPU.
        /// </summary>
        public override void CompTick()
        {
            base.CompTick();

            if (!Station.HasPower)   return;
            if (!Station.IsOccupied) return;

            Pawn pawn = Station.CurrentOccupant;
            if (pawn == null || pawn.Dead) return;

            if (Find.TickManager.TicksGame % Props.repairTickInterval != 0) return;

            // Verificar acero antes de curar: Building.Tick() ya consumió en este tick.
            // Si el buffer quedó vacío, no aplicar curación gratuita.
            if (!Station.HasSteel) return;

            ApplyRepairTick(pawn);
        }

        /// <summary>
        /// Aplica repairSpeedPerTick a cada Hediff_Injury activa (no permanente) del mecanoid.
        /// Llama a OnRepairComplete cuando la salud supera el 99%.
        /// </summary>
        private void ApplyRepairTick(Pawn mechanoid)
        {
            List<Hediff_Injury> injuries = mechanoid.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(h => !(h.TryGetComp<HediffComp_GetsPermanent>()?.IsPermanent ?? false))
                .ToList();

            foreach (Hediff_Injury injury in injuries)
                injury.Heal(Props.repairSpeedPerTick);

            if (mechanoid.health.summaryHealth.SummaryHealthPercent >= 0.99f)
                OnRepairComplete(mechanoid);
        }

        /// <summary>
        /// Notifica al jugador y limpia el ocupante cuando la reparación termina.
        /// El driver detecta CurrentOccupant == null en su tickAction y termina el job.
        /// </summary>
        private void OnRepairComplete(Pawn mechanoid)
        {
            Messages.Message(
                "RRS_LetterRepairCompleteText".Translate(mechanoid.LabelShort),
                mechanoid,
                MessageTypeDefOf.PositiveEvent
            );

            Station.NotifyOccupantLeft();
        }
    }
}
