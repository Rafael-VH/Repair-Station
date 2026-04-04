using RimWorld;
using Verse;

namespace RobotRepairStation
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  COMP PROPERTIES
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Propiedades configurables del componente de reparación.
    /// Leídas desde el XML del ThingDef (bloque CompProperties_RobotRepairStation).
    /// Todos los campos tienen valores por defecto para compatibilidad con saves
    /// que no tengan el campo explícito.
    /// </summary>
    public class CompProperties_RobotRepairStation : CompProperties
    {
        /// <summary>Fracción de salud (0–1) bajo la que el mecanoid busca reparación.</summary>
        public float repairHealthThreshold = 0.5f;

        /// <summary>HP curados por tick por cada lesión activa.</summary>
        public float repairSpeedPerTick = 0.0005f;

        /// <summary>Unidades de acero consumidas por ciclo de reparación.</summary>
        public int steelPerRepairCycle = 1;

        /// <summary>
        /// Ticks entre cada ciclo de curación y consumo de acero (~8.3 s a ×1 velocidad).
        /// Controla tanto la granularidad del consumo de recursos como el coste de CPU.
        /// </summary>
        public int repairTickInterval = 500;

        /// <summary>Distancia máxima en celdas para que un mecanoid detecte esta estación.</summary>
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
    /// Componente adjunto a <see cref="Building_RobotRepairStation"/>.
    /// Aplica curación a las lesiones activas del mecanoid docked cada
    /// <see cref="CompProperties_RobotRepairStation.repairTickInterval"/> ticks.
    ///
    /// El mismo offset de tick usado en <see cref="Building_RobotRepairStation.Tick"/>
    /// se aplica aquí para que ambos ciclos (consumo de acero y curación) estén
    /// sincronizados y distribuidos uniformemente entre múltiples instancias.
    /// </summary>
    public class CompRobotRepairStation : ThingComp
    {
        public CompProperties_RobotRepairStation Props =>
            (CompProperties_RobotRepairStation)props;

        private Building_RobotRepairStation Station =>
            (Building_RobotRepairStation)parent;

        /// <summary>
        /// Ejecutado cada tick por RimWorld (requiere <c>tickerType Normal</c> en el ThingDef).
        /// Sale inmediatamente si no hay condiciones para curar, minimizando el coste de CPU.
        /// </summary>
        public override void CompTick()
        {
            base.CompTick();

            if (!Station.HasPower)   return;
            if (!Station.IsOccupied) return;

            Pawn pawn = Station.CurrentOccupant;
            if (pawn == null || pawn.Dead) return;

            // El mismo offset garantiza que el ciclo de curación y el de consumo de acero
            // se ejecuten en el mismo tick, y que múltiples estaciones no coincidan todas
            // en el mismo frame exacto.
            if ((Find.TickManager.TicksGame + parent.thingIDNumber.HashOffset()) % Props.repairTickInterval != 0)
                return;

            // El Building.Tick() ya consumió (o intentó consumir) acero en este mismo tick.
            // Si el buffer quedó vacío y el ocupante fue expulsado, no aplicar curación.
            if (!Station.HasSteel) return;

            ApplyRepairTick(pawn);
        }

        /// <summary>
        /// Aplica <see cref="CompProperties_RobotRepairStation.repairSpeedPerTick"/> a cada
        /// <see cref="Hediff_Injury"/> activa (no permanente) del mecanoid.
        ///
        /// Se itera directamente sobre <c>hediffs</c> sin crear una lista intermedia
        /// para evitar allocaciones innecesarias en un método llamado frecuentemente.
        /// Las lesiones permanentes (p. ej. miembros perdidos) se omiten de forma
        /// intencional: este edificio no regenera partes del cuerpo.
        /// </summary>
        private void ApplyRepairTick(Pawn mechanoid)
        {
            foreach (Hediff hediff in mechanoid.health.hediffSet.hediffs)
            {
                if (hediff is Hediff_Injury injury &&
                    !(injury.TryGetComp<HediffComp_GetsPermanent>()?.IsPermanent ?? false))
                {
                    injury.Heal(Props.repairSpeedPerTick);
                }
            }

            if (mechanoid.health.summaryHealth.SummaryHealthPercent >= 0.99f)
                OnRepairComplete(mechanoid);
        }

        /// <summary>
        /// Notifica al jugador y libera el slot de ocupante cuando la reparación termina.
        /// El driver de <see cref="JobDriver_RepairAtStation"/> detecta
        /// <c>CurrentOccupant == null</c> en su <c>tickAction</c> y finaliza el job.
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
