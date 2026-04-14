using UnityEngine;
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

            // Advertencia temprana si falta la textura principal del edificio.
            // ContentFinder devuelve null (no lanza excepción) cuando reportFailure=false.
            if (ContentFinder<Texture2D>.Get("Things/Buildings/RobotRepairStation", false) == null)
            {
                Log.Warning(
                    "[RobotRepairStation] Textura no encontrada. " +
                    "Coloca RobotRepairStation.png (128×128 px) en " +
                    "Textures/Things/Buildings/ para eliminar este aviso.");
            }
        }
    }
}
