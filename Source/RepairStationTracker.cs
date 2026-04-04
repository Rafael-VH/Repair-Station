using System.Collections.Generic;
using Verse;

namespace RobotRepairStation
{
    /// <summary>
    /// MapComponent que mantiene un registro centralizado de todas las
    /// Building_RobotRepairStation presentes en el mapa.
    ///
    /// RimWorld instancia automáticamente este componente al crear o cargar cada mapa,
    /// gracias a la declaración en 1.6/Defs/MapComponentDefs/MapComponentDefs.xml.
    /// Las estaciones se re-registran vía SpawnSetup en cada carga, por lo que
    /// la lista interna no necesita serialización.
    /// </summary>
    public class RepairStationTracker : MapComponent
    {
        private readonly List<Building_RobotRepairStation> stations =
            new List<Building_RobotRepairStation>();

        /// <summary>Vista de solo lectura de todas las estaciones registradas en este mapa.</summary>
        public IReadOnlyList<Building_RobotRepairStation> AllStations => stations;

        public RepairStationTracker(Map map) : base(map) { }

        /// <summary>
        /// Obtiene el tracker del mapa usando la API de RimWorld (<c>map.GetComponent&lt;T&gt;()</c>).
        /// Solo crea uno nuevo si no existe, lo cual sirve de red de seguridad para casos
        /// donde el MapComponentDef no pudo instanciar el componente correctamente.
        /// </summary>
        public static RepairStationTracker GetOrCreate(Map map)
        {
            var existing = map.GetComponent<RepairStationTracker>();
            if (existing != null) return existing;

            var tracker = new RepairStationTracker(map);
            map.components.Add(tracker);
            return tracker;
        }

        /// <summary>
        /// Registra una estación en este mapa. Llamado desde
        /// <see cref="Building_RobotRepairStation.SpawnSetup"/>.
        /// </summary>
        public void Register(Building_RobotRepairStation station)
        {
            if (!stations.Contains(station))
                stations.Add(station);
        }

        /// <summary>
        /// Desregistra una estación de este mapa. Llamado desde
        /// <see cref="Building_RobotRepairStation.DeSpawn"/>.
        /// </summary>
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
