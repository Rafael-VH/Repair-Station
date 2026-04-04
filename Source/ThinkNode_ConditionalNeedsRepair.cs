using RimWorld;
using Verse;
using Verse.AI;

namespace RobotRepairStation
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  NODO CONDICIONAL DEL THINK TREE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Nodo condicional inyectado al inicio del ThinkNode_Priority de MechanoidConstant
    /// mediante Patches/MechanoidThinkTree.xml. Evalúa si el mecanoid necesita reparación.
    ///
    /// Orden de evaluación (de más barata a más cara):
    ///   1. ¿Es mecanoid?
    ///   2. ¿Es del jugador?
    ///   3. ¿Ya está en un job de reparación (activo o en cola)?
    ///   4. ¿Hay una estación válida y alcanzable? (operación más costosa, al final)
    ///   5. ¿La salud está bajo el umbral configurado?
    /// </summary>
    public class ThinkNode_ConditionalNeedsRepair : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            if (!pawn.RaceProps.IsMechanoid)      return false;
            if (pawn.Faction != Faction.OfPlayer)  return false;

            // Evitar interrumpir una reparación ya en curso.
            if (pawn.CurJob?.def == RRS_JobDefOf.RRS_RepairAtStation   ||
                pawn.CurJob?.def == RRS_JobDefOf.RRS_GoToRepairStation)
                return false;

            // Búsqueda de estación (operación más cara): solo si pasó los filtros anteriores.
            var comp = RepairStationUtility.FindBestRepairStationComp(pawn);
            if (comp == null) return false;

            return pawn.health.summaryHealth.SummaryHealthPercent < comp.Props.repairHealthThreshold;
        }
    }


    // ═══════════════════════════════════════════════════════════════════════════
    //  JOB GIVER
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emite el job <c>RRS_GoToRepairStation</c> cuando hay una estación disponible.
    /// Hijo directo de <see cref="ThinkNode_ConditionalNeedsRepair"/> en el ThinkTree.
    ///
    /// Repite la búsqueda de estación porque los ThinkNodes no comparten estado entre sí;
    /// es una limitación del sistema de ThinkTree de RimWorld.
    /// </summary>
    public class JobGiver_GoToRepairStation : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            Building_RobotRepairStation station =
                RepairStationUtility.FindBestRepairStation(pawn);

            if (station == null) return null;

            // No competir si otro pawn de la misma facción ya tiene la estación reservada.
            var reservationManager = pawn.Map?.reservationManager;
            if (reservationManager != null
                && reservationManager.IsReservedByAnyoneOf(station, pawn.Faction)
                && !reservationManager.ReservedBy(station, pawn))
            {
                return null;
            }

            return JobMaker.MakeJob(RRS_JobDefOf.RRS_GoToRepairStation, station);
        }
    }


    // ═══════════════════════════════════════════════════════════════════════════
    //  UTILIDADES DE BÚSQUEDA
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Centraliza la lógica de búsqueda de estaciones para evitar duplicarla en
    /// <see cref="ThinkNode_ConditionalNeedsRepair"/> y <see cref="JobGiver_GoToRepairStation"/>.
    /// </summary>
    public static class RepairStationUtility
    {
        /// <summary>
        /// Encuentra la estación válida más cercana al pawn.
        ///
        /// Criterios de filtrado (ordenados de más baratos a más caros):
        /// <list type="number">
        ///   <item>Estación no destruida.</item>
        ///   <item>Estación con energía activa.</item>
        ///   <item>No ocupada por otro mecanoid distinto al solicitante.</item>
        ///   <item>Alcanzable por el pawn (<c>CanReach</c>, operación costosa).</item>
        ///   <item>Dentro del rango <c>maxRepairRange</c> configurado.</item>
        /// </list>
        ///
        /// Retorna <c>null</c> si el mapa del pawn es <c>null</c> (p. ej. en caravana)
        /// o si ninguna estación cumple los criterios.
        /// </summary>
        public static Building_RobotRepairStation FindBestRepairStation(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            var tracker = RepairStationTracker.GetOrCreate(pawn.Map);
            Building_RobotRepairStation best = null;
            float bestDist = float.MaxValue;

            foreach (var station in tracker.AllStations)
            {
                if (station == null || station.Destroyed) continue;
                if (!station.HasPower)                    continue;
                if (station.IsOccupied && station.CurrentOccupant != pawn) continue;

                // CanReach es relativamente caro; va al final, después de los filtros baratos.
                if (!pawn.CanReach(station, PathEndMode.InteractionCell, Danger.Deadly)) continue;

                var comp = station.GetComp<CompRobotRepairStation>();
                float maxRange = comp?.Props.maxRepairRange ?? 30f;

                float dist = pawn.Position.DistanceTo(station.Position);
                if (dist > maxRange) continue;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best     = station;
                }
            }

            return best;
        }

        /// <summary>
        /// Variante de <see cref="FindBestRepairStation"/> que devuelve directamente
        /// el <see cref="CompRobotRepairStation"/> de la mejor estación encontrada.
        /// Útil cuando solo se necesitan las propiedades de configuración del comp.
        /// </summary>
        public static CompRobotRepairStation FindBestRepairStationComp(Pawn pawn)
        {
            var station = FindBestRepairStation(pawn);
            return station?.GetComp<CompRobotRepairStation>();
        }
    }
}
