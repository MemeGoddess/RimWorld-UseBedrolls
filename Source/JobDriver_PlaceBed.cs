using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;
using RimWorld;
using HarmonyLib;

namespace UseBedrolls
{
	[DefOf]
	public static class JobDefOf
	{
		public static JobDef PlaceBedroll;
		public static JobDef TakeBedroll;
	}

	public static class ClaimTheGoddamnBed
	{
		public static void ClaimTheGoddamnBedOkay(this Pawn pawn, Building_Bed newBed)
		{
			if (pawn.IsSlaveOfColony)
				newBed.ForOwnerType = BedOwnerType.Slave;

			pawn.ownership?.ClaimBedIfNonMedical(newBed);
		}
	}

	public class JobDriver_PlaceBedroll : JobDriver
	{
		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return true;
		}
		protected override IEnumerable<Toil> MakeNewToils()
		{
			//Clean up any spare blueprints
			AddFinishAction(delegate
			{
				Blueprint blueprint = TargetB.Thing as Blueprint;
				if (!blueprint?.Destroyed ?? false)
				{ 
					Log.Message($"Cleaning up {blueprint}");
					blueprint.Destroy(DestroyMode.Vanish);
				}
			});

			job.count = 1;
			Thing bedroll = pawn.CurJob.targetA.Thing;

			if (bedroll.Spawned)
			{
				Log.Message($"{pawn} needs to pick up {bedroll}");
				yield return Toils_Reserve.Reserve(TargetIndex.A);
				yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
				yield return Toils_Haul.TakeToInventory(TargetIndex.A, 1);
			}
			yield return Toils_Misc.TakeItemFromInventoryToCarrier(pawn, TargetIndex.A);
			yield return Toils_Haul.CarryHauledThingToContainer();
			yield return Toils_Construct.MakeSolidThingFromBlueprintIfNecessary(TargetIndex.B, TargetIndex.C);
			//yield return Toils_Haul.DepositHauledThingInContainer(TargetIndex.B, TargetIndex.C);//1.0 game destroys minified thing, but its job still does Deposit?
			Toil restInBed = new Toil();
			restInBed.initAction = delegate
			{
				Pawn pawn = restInBed.actor;
				Job curJob = pawn.jobs.curJob;
				Building_Bed bed = curJob.targetB.Thing as Building_Bed;

				Log.Message($"{pawn} placing bed {bed}");

				pawn.ClaimTheGoddamnBedOkay(bed);
				//placedBeds after Claim because claiming can set forowner which removes owners which removes the placedBed.
				pawn.Map.GetComponent<PlacedBedsMapComponent>().placedBeds[pawn] = bed;

				Job restJob = new Job(RimWorld.JobDefOf.LayDown, TargetB);
				pawn.jobs.StartJob(restJob, JobCondition.Succeeded);
			};
			yield return restInBed;
		}
	}

	public class JobDriver_TakeBedroll : JobDriver_Uninstall
	{
	private float workLeft;
	private float totalNeededWork;
		public override void Notify_Starting()
		{
			base.Notify_Starting();
			if(pawn.Map.designationManager.DesignationOn(Target)?.def != DesignationDefOf.Uninstall)
				pawn.Map.designationManager.AddDesignation(new Designation(Target, DesignationDefOf.Uninstall));

		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			if (Designation != null)
				this.FailOnThingMissingDesignation(TargetIndex.A, Designation);

			this.FailOnForbidden(TargetIndex.A);

			this.FailOn(() =>
			{
				CompExplosive comp;
				return Building.TryGetComp(out comp) && comp.wickStarted;
			});

			yield return Toils_Goto.GotoCell(TargetIndex.A, Target is Building_Trap ? PathEndMode.OnCell : PathEndMode.Touch);

			Toil doWork = ToilMaker.MakeToil(nameof(MakeNewToils))
				.FailOnDestroyedNullOrForbidden(TargetIndex.A);

			doWork.initAction = delegate
			{
				totalNeededWork = TotalNeededWork;
				workLeft = totalNeededWork;

				if (!Building.Spawned)
					doWork.actor.jobs.curDriver.ReadyForNextToil();
			};

			doWork.tickIntervalAction = delegate (int delta)
			{
				workLeft -= pawn.GetStatValue(StatDefOf.ConstructionSpeed) * 1.7f * delta;
				TickActionInterval(delta);
				if (workLeft <= 0f)
					doWork.actor.jobs.curDriver.ReadyForNextToil();
			};

			doWork.defaultCompleteMode = ToilCompleteMode.Never;

			if (WorkEffecter != null)
				doWork.WithEffect(WorkEffecter, TargetIndex.A);

			doWork.WithProgressBar(TargetIndex.A, () => 1f - workLeft / totalNeededWork);
			doWork.activeSkill = () => SkillDefOf.Construction;

			yield return doWork;

			Toil finish = ToilMaker.MakeToil(nameof(MakeNewToils));
			finish.initAction = delegate
			{
				FinishedRemoving();
			};
			finish.defaultCompleteMode = ToilCompleteMode.Instant;

			yield return finish;
		}

		protected override void FinishedRemoving()
		{
			Log.Message($"Removing placed beds for {pawn}");
			pawn.Map.GetComponent<PlacedBedsMapComponent>().placedBeds.Remove(pawn);

			Thing minifiedThing = FindExistingMinifiedBed();
			if (minifiedThing == null && Building.Spawned)
				minifiedThing = Building.Uninstall();

			if (minifiedThing != null)
			{
				if (minifiedThing.Spawned)
					pawn.inventory.GetDirectlyHeldThings().TryAdd(minifiedThing.SplitOff(1));
				else if (pawn.inventory.innerContainer.Contains(minifiedThing))
					Log.Message($"{pawn} already has {minifiedThing}");
				else
					pawn.inventory.innerContainer.TryAddOrTransfer(minifiedThing);
			}

			if(HomeBedComp.Get(pawn, out Building_Bed bed))
			{
				Log.Message($"Re-claming Home bed {bed} for {pawn}");
				pawn.ClaimTheGoddamnBedOkay(bed);
			}
		}

		private Thing FindExistingMinifiedBed()
		{
			Thing inventoryBed = pawn.inventory?.innerContainer?.FirstOrDefault(t => t.GetInnerIfMinified() == Building);
			if (inventoryBed != null)
				return inventoryBed;

			return pawn.Map?.listerThings?.ThingsInGroup(ThingRequestGroup.MinifiedThing)
				?.FirstOrDefault(t => t.GetInnerIfMinified() == Building);
		}
	}
	
	[HarmonyPatch(typeof(GenConstruct), "BlocksConstruction")]
	static class PawnBlockConstruction
	{
		static bool Prefix(ref bool __result, Thing t, Thing constructible)
		{
			if (t is Pawn && 
				(constructible is Building_Bed || 
				(constructible is Blueprint_Install b && b.MiniToInstallOrBuildingToReinstall.GetInnerIfMinified() is Building_Bed)))
			{
				__result = false;
				return false;
			}
			return true;
		}
	}
}