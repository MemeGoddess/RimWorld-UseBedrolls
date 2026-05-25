using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;
using RimWorld;

namespace UseBedrolls
{
	class JobGiver_TakeBedBack : ThinkNode_JobGiver
	{
		private const float RestThresholdAnything = 0.3f;
		private const float RestThresholdMeditate = 0.16f;

		public override float GetPriority(Pawn pawn)
		{
			if (ShouldKeepPlacedBed(pawn))
				return 0f;

			var rester = pawn.thinker.GetMainTreeThinkNode<JobGiver_GetRest>();
			return (8f - (rester?.GetPriority(pawn) ?? 0f));
		}

		protected override Job TryGiveJob(Pawn pawn)
		{
			if (!pawn.IsColonistPlayerControlled) return null;
			if (ShouldKeepPlacedBed(pawn)) return null;
			
			var placedBeds = pawn.Map.GetComponent<PlacedBedsMapComponent>().placedBeds;
  			if (placedBeds.TryGetValue(pawn, out Building_Bed bed))
			  {
				Log.Message($"{pawn} taking back bed {bed}");
				if (pawn.CanReserve(bed))
					return new Job(JobDefOf.TakeBedroll, bed) { ignoreForbidden = true };
			}

			return null;
		}

		private static bool ShouldKeepPlacedBed(Pawn pawn)
		{
			return NeedsRestNow(pawn) && !HasTemporarySleepBlocker(pawn);
		}

		private static bool NeedsRestNow(Pawn pawn)
		{
			var rest = pawn.needs?.rest;
			if (rest == null)
				return false;

			var assignment = pawn.timetable?.CurrentAssignment ?? TimeAssignmentDefOf.Anything;
			if (assignment == TimeAssignmentDefOf.Work)
				return false;
			if (assignment == TimeAssignmentDefOf.Meditate)
				return rest.CurLevel < RestThresholdMeditate;
			if (assignment == TimeAssignmentDefOf.Anything || assignment == TimeAssignmentDefOf.Joy)
				return rest.CurLevel < RestThresholdAnything;
			if (assignment == TimeAssignmentDefOf.Sleep)
				return true;

			return assignment.allowRest && rest.CurLevel < RestThresholdAnything;
		}

		private static bool HasTemporarySleepBlocker(Pawn pawn)
		{
			if (pawn.mindState != null && Find.TickManager.TicksGame < pawn.mindState.canSleepTick)
				return true;
			if (RestUtility.DisturbancePreventsLyingDown(pawn))
				return true;
			if (pawn.needs?.food?.Starving ?? false)
				return !pawn.ageTracker.CurLifeStage.canSleepWhenStarving;
			if (pawn.health.hediffSet.HasHediffBlocksSleeping())
				return true;
			if (pawn.CurJobDef != null && !pawn.CurJobDef.sleepCanInterrupt)
				return true;

			return false;
		}
	}
}
