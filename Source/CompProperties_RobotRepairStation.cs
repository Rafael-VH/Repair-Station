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
    ///
    /// NOTA: repairHealthThreshold NO se lee aquí en tiempo de ejecución;
    /// el valor ajustable por el jugador vive en CompRobotRepairStation.repairThreshold
    /// (campo serializado). Este valor de props actúa solo como valor inicial por defecto.
    /// </summary>
    public class CompProperties_RobotRepairStation : CompProperties
    {
        /// <summary>
        /// Umbral de salud inicial (0–1). El jugador puede sobreescribirlo
        /// en juego mediante el gizmo; el valor real en tiempo de ejecución
        /// reside en <see cref="CompRobotRepairStation.repairThreshold"/>.
        /// </summary>
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

        /// <summary>Distancia máxima en celdas para que un mecanoide detecte esta estación.</summary>
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
    /// Aplica curación a las lesiones activas del mecanoide docked cada
    /// <see cref="CompProperties_RobotRepairStation.repairTickInterval"/> ticks.
    ///
    /// Responsabilidades de este comp:
    /// - Mantener el umbral de salud ajustado por el jugador (<see cref="repairThreshold"/>),
    ///   serializado con Scribe_Values para que persista entre sesiones.
    /// - Aplicar curación tick a tick cuando hay ocupante con acero disponible.
    /// - Disparar la carta de reparación completa al llegar al 99 % de salud.
    ///
    /// El mismo offset de tick usado en <see cref="Building_RobotRepairStation.Tick"/>
    /// se aplica aquí para que ambos ciclos (consumo de acero y curación) estén
    /// sincronizados y distribuidos uniformemente entre múltiples instancias.
    /// </summary>
    public class CompRobotRepairStation : ThingComp
    {
        // ─── Estado serializable ──────────────────────────────────────────────

        /// <summary>
        /// Umbral de salud (0–1) ajustado por el jugador para ESTA instancia.
        /// Inicializado con el valor de Props en la primera carga.
        /// Modificable en juego desde el gizmo de la estación.
        /// </summary>
        public float repairThreshold = -1f; // -1 = "no inicializado aún"

        // ─── Propiedades ──────────────────────────────────────────────────────

        public CompProperties_RobotRepairStation Props =>
            (CompProperties_RobotRepairStation)props;

        private Building_RobotRepairStation Station =>
            (Building_RobotRepairStation)parent;

        // ─── Ciclo de vida ────────────────────────────────────────────────────

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // Primera vez que se coloca o se carga un save antiguo sin el campo:
            // copiar el valor de Props como valor inicial por instancia.
            if (repairThreshold < 0f)
                repairThreshold = Props.repairHealthThreshold;
        }

        // ─── Serialización ────────────────────────────────────────────────────

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref repairThreshold, "repairThreshold", Props.repairHealthThreshold);

            // Guardia de seguridad: si el valor quedó corrupto, restaurar el default.
            if (repairThreshold < 0f || repairThreshold > 1f)
                repairThreshold = Props.repairHealthThreshold;
        }

        // ─── Tick ─────────────────────────────────────────────────────────────

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

        // ─── Lógica de curación ───────────────────────────────────────────────

        /// <summary>
        /// Aplica <see cref="CompProperties_RobotRepairStation.repairSpeedPerTick"/> a cada
        /// <see cref="Hediff_Injury"/> activa (no permanente) del mecanoide.
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
        /// Notifica al jugador mediante una carta persistente y libera el slot de ocupante
        /// cuando la reparación termina. El driver de <see cref="JobDriver_RepairAtStation"/>
        /// detecta <c>CurrentOccupant == null</c> en su <c>tickAction</c> y finaliza el job.
        /// </summary>
        private void OnRepairComplete(Pawn mechanoid)
        {
            // Carta persistente (aparece en el historial de letras, no desaparece sola).
            Find.LetterStack.ReceiveLetter(
                "RRS_LetterRepairCompleteLabel".Translate(),
                "RRS_LetterRepairCompleteText".Translate(mechanoid.LabelShort),
                LetterDefOf.PositiveEvent,
                mechanoid
            );

            // Efecto visual en la posición del mecanoide al completar la reparación.
            if (mechanoid.Spawned)
                FleckMaker.ThrowLightningGlow(mechanoid.DrawPos, mechanoid.Map, 2f);

            Station.NotifyOccupantLeft();
        }
    }
}
