using RimWorld;
using Verse;

namespace RobotRepairStation
{
    /// <summary>
    /// Registro estático de los JobDefs propios del mod.
    /// El atributo [DefOf] hace que RimWorld inyecte automáticamente las referencias
    /// después de que todas las definiciones XML han sido cargadas.
    /// </summary>
    [DefOf]
    public static class RRS_JobDefOf
    {
        /// <summary>
        /// Job de navegación: el mecanoide se desplaza hasta la InteractionCell de la estación.
        /// Emitido por JobGiver_GoToRepairStation, ejecutado por JobDriver_GoToRepairStation.
        /// </summary>
        public static JobDef RRS_GoToRepairStation;

        /// <summary>
        /// Job de reparación: el mecanoide permanece en la estación mientras CompRobotRepairStation
        /// aplica curación tick a tick. Termina cuando CurrentOccupant pasa a null.
        /// </summary>
        public static JobDef RRS_RepairAtStation;

        static RRS_JobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RRS_JobDefOf));
        }
    }
}
