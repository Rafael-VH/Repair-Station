using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RobotRepairStation
{
    /// <summary>
    /// Driver del job RRS_GoToRepairStation.
    /// Mueve el mecanoide hasta la InteractionCell de la estación y, al llegar,
    /// intenta registrarse como ocupante y encola RRS_RepairAtStation.
    ///
    /// Flujo de toils:
    ///   1. GotoThing — caminar hasta la celda de interacción.
    ///   2. dock (Instant) — TryAcceptOccupant + EnqueueFirst(RepairAtStation).
    /// </summary>
    public class JobDriver_GoToRepairStation : JobDriver
    {
        private Building_RobotRepairStation Station =>
            (Building_RobotRepairStation)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Condiciones de fallo evaluadas automáticamente cada tick.
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOnForbidden(TargetIndex.A);
            this.FailOn(() => !Station.HasPower);
            this.FailOn(() => Station.IsOccupied && Station.CurrentOccupant != pawn);

            // Toil 1: caminar hasta la celda de interacción.
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            // Toil 2: intentar ocupar la estación (Instant — se completa en el mismo tick).
            var dock = new Toil();
            dock.initAction = () =>
            {
                if (Station.TryAcceptOccupant(pawn))
                {
                    // Ocupante registrado → encolar job de reparación y terminar.
                    var repairJob = JobMaker.MakeJob(RRS_JobDefOf.RRS_RepairAtStation, Station);
                    pawn.jobs.jobQueue.EnqueueFirst(repairJob);
                    EndJobWith(JobCondition.Succeeded);
                }
                else
                {
                    // Race condition: estación ocupada entre llegada y dock → reintentar.
                    EndJobWith(JobCondition.Incompletable);
                }
            };
            dock.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return dock;
        }
    }
}
