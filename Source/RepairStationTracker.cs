using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RobotRepairStation
{
    /// <summary>
    /// Componente de mapa que mantiene un registro centralizado de todas las
    /// Building_RobotRepairStation presentes en el mapa.
    /// Declarado en 1.6/Defs/MapComponentDefs/MapComponentDefs.xml para que
    /// RimWorld lo instancie automáticamente antes de que cualquier ThinkTree se ejecute.
    /// </summary>
    public class RepairStationTracker : MapComponent
    {
        private readonly List<Building_RobotRepairStation> stations =
            new List<Building_RobotRepairStation>();

        /// <summary>Vista de solo lectura de todas las estaciones registradas.</summary>
        public IReadOnlyList<Building_RobotRepairStation> AllStations => stations;

        public RepairStationTracker(Map map) : base(map) { }

        /// <summary>
        /// Obtiene el tracker existente del mapa o crea uno nuevo si no existe.
        /// Red de seguridad: el MapComponentDef garantiza que el componente ya existe
        /// antes de que se llame a este método en condiciones normales.
        /// </summary>
        public static RepairStationTracker GetOrCreate(Map map)
        {
            var existing = map.components.OfType<RepairStationTracker>().FirstOrDefault();
            if (existing != null) return existing;

            var tracker = new RepairStationTracker(map);
            map.components.Add(tracker);
            return tracker;
        }

        /// <summary>Registra una estación. Llamado desde Building_RobotRepairStation.SpawnSetup.</summary>
        public void Register(Building_RobotRepairStation station)
        {
            if (!stations.Contains(station))
                stations.Add(station);
        }

        /// <summary>Desregistra una estación. Llamado desde Building_RobotRepairStation.DeSpawn.</summary>
        public void Deregister(Building_RobotRepairStation station)
        {
            stations.Remove(station);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // La lista no se serializa: las estaciones se re-registran
            // automáticamente durante SpawnSetup al cargar el mapa.
        }
    }
}
