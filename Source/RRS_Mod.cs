using Verse;

namespace RobotRepairStation
{
    /// <summary>
    /// Punto de entrada del mod Robot Repair Station.
    /// El atributo [StaticConstructorOnStartup] garantiza que el bloque estático
    /// se ejecuta una sola vez cuando RimWorld termina de cargar todos los mods.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RRS_Mod
    {
        static RRS_Mod()
        {
            Log.Message("[RobotRepairStation] Mod cargado correctamente.");
        }
    }
}
