using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RobotRepairStation
{
    /// <summary>
    /// Driver del job RRS_RepairAtStation.
    /// Mantiene al mecanoide quieto en la estación mientras CompRobotRepairStation
    /// aplica curación tick a tick. Termina cuando CurrentOccupant pasa a null.
    ///
    /// Este job es siempre encolado por JobDriver_GoToRepairStation.
    /// IsContinuation() devuelve true para ese job, permitiendo a RimWorld
    /// reutilizar la reserva existente sin exigir una nueva.
    /// </summary>
    public class JobDriver_RepairAtStation : JobDriver
    {
        private Building_RobotRepairStation Station =>
            (Building_RobotRepairStation)job.targetA.Thing;

        /// <summary>
        /// No reserva de nuevo: la reserva ya existe desde JobDriver_GoToRepairStation.
        /// IsContinuation() garantiza que RimWorld la reutiliza.
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !Station.HasPower);

            var wait = new Toil();

            wait.initAction = () =>
            {
                pawn.pather.StopDead();
                pawn.rotationTracker.FaceTarget(Station);
            };

            wait.tickAction = () =>
            {
                if (Station.CurrentOccupant != pawn)
                    EndJobWith(JobCondition.Succeeded);
            };

            wait.handlingFacing      = true;
            wait.defaultCompleteMode = ToilCompleteMode.Never;

            yield return wait;
        }

        /// <summary>
        /// Override vacío intencional: la interrupción por daño está desactivada
        /// a nivel de JobDef (checkOverrideOnDamage=false).
        /// </summary>
        public override void Notify_DamageTaken(DamageInfo dinfo)
        {
            base.Notify_DamageTaken(dinfo);
        }

        /// <summary>
        /// Indica que este job es continuación directa del job de navegación
        /// hacia la misma estación, permitiendo reutilizar su reserva.
        /// </summary>
        public override bool IsContinuation(Job j)
        {
            return j.def == RRS_JobDefOf.RRS_GoToRepairStation
                && j.targetA == job.targetA;
        }
    }
}
