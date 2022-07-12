using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimWorld;

namespace LivingBuildingFramework
{
    internal class Building_HealthTracker
    {
        private LivingBuilding building;
        private BuildingHealthState healthState = BuildingHealthState.Alive;
        [Unsaved(false)]
        public Effecter woundedEffecter;
        [Unsaved(false)]
        public Effecter deflectionEffecter;
        //public bool forceIncap;
        //public bool beCarriedByCaravanIfSick;
        //public bool killedByRitual;
        //public int lastReceivedNeuralSuperchargeTick = -1;
        public HediffSet hediffSet;
        //public buildingCapacitiesHandler capacities;
        public BillStack surgeryBills;
        public SummaryHealthHandler summaryHealth;
        public ImmunityHandler immunity;

        public BuildingHealthState State => this.healthState;

        public bool Dividing => this.healthState == BuildingHealthState.Dividing;

        public bool Dying => this.healthState == BuildingHealthState.Dying;

        public float LethalDamageThreshold => 150f * this.building.HealthScale;

        public bool InPainShock => (double)this.hediffSet.PainTotal >= (double)this.building.GetStatValue(StatDefOf.PainShockThreshold);

        public Building_HealthTracker(Building building)
        {
            this.building = building;
            this.hediffSet = new HediffSet(building);
            //this.capacities = new buildingCapacitiesHandler(building);
            this.summaryHealth = new SummaryHealthHandler(building);
            this.surgeryBills = new BillStack((IBillGiver)building);
            this.immunity = new ImmunityHandler(building);
            //this.beCarriedByCaravanIfSick = building.RaceProps.Humanlike;
        }

        public void Reset()
        {
            this.healthState = BuildingHealthState.Mobile;
            this.hediffSet.Clear();
            this.capacities.Clear();
            this.summaryHealth.Notify_HealthChanged();
            this.surgeryBills.Clear();
            this.immunity = new ImmunityHandler(this.building);
        }

        public void ExposeData()
        {
            Scribe_Values.Look<BuildingHealthState>(ref this.healthState, "healthState", BuildingHealthState.Mobile);
            Scribe_Values.Look<bool>(ref this.forceIncap, "forceIncap");
            Scribe_Values.Look<bool>(ref this.beCarriedByCaravanIfSick, "beCarriedByCaravanIfSick", true);
            Scribe_Values.Look<bool>(ref this.killedByRitual, "killedByRitual");
            Scribe_Values.Look<int>(ref this.lastReceivedNeuralSuperchargeTick, "lastReceivedNeuralSuperchargeTick", -1);
            Scribe_Deep.Look<HediffSet>(ref this.hediffSet, "hediffSet", (object)this.building);
            Scribe_Deep.Look<BillStack>(ref this.surgeryBills, "surgeryBills", (object)this.building);
            Scribe_Deep.Look<ImmunityHandler>(ref this.immunity, "immunity", (object)this.building);
        }

        public Hediff AddHediff(
          HediffDef def,
          BodyPartRecord part = null,
          DamageInfo? dinfo = null,
          DamageWorker.DamageResult result = null)
        {
            Hediff hediff = HediffMaker.MakeHediff(def, this.building);
            this.AddHediff(hediff, part, dinfo, result);
            return hediff;
        }

        public void AddHediff(
          Hediff hediff,
          BodyPartRecord part = null,
          DamageInfo? dinfo = null,
          DamageWorker.DamageResult result = null)
        {
            if (part != null)
                hediff.Part = part;
            this.hediffSet.AddDirect(hediff, dinfo, result);
            this.CheckForStateChange(dinfo, hediff);
            if (this.building.RaceProps.hediffGiverSets == null)
                return;
            for (int index1 = 0; index1 < this.building.RaceProps.hediffGiverSets.Count; ++index1)
            {
                HediffGiverSetDef hediffGiverSet = this.building.RaceProps.hediffGiverSets[index1];
                for (int index2 = 0; index2 < hediffGiverSet.hediffGivers.Count; ++index2)
                    hediffGiverSet.hediffGivers[index2].OnHediffAdded(this.building, hediff);
            }
        }

        public void RemoveHediff(Hediff hediff)
        {
            this.hediffSet.hediffs.Remove(hediff);
            hediff.PostRemoved();
            this.Notify_HediffChanged((Hediff)null);
        }

        public void RemoveAllHediffs()
        {
            for (int index = this.hediffSet.hediffs.Count - 1; index >= 0; --index)
                this.RemoveHediff(this.hediffSet.hediffs[index]);
        }

        public void Notify_HediffChanged(Hediff hediff)
        {
            this.hediffSet.DirtyCache();
            this.CheckForStateChange(new DamageInfo?(), hediff);
        }

        public void Notify_UsedVerb(Verb verb, LocalTargetInfo target)
        {
            foreach (Hediff hediff in this.hediffSet.hediffs)
                hediff.Notify_buildingUsedVerb(verb, target);
        }

        public void PreApplyDamage(DamageInfo dinfo, out bool absorbed)
        {
            Faction homeFaction = this.building.HomeFaction;
            if (dinfo.Instigator != null && homeFaction != null && homeFaction.IsPlayer && !this.building.InAggroMentalState)
            {
                building instigator = dinfo.Instigator as building;
                if (dinfo.InstigatorGuilty && instigator != null && instigator.guilt != null && instigator.mindState != null)
                    instigator.guilt.Notify_Guilty();
            }
            if (this.building.Sbuildinged)
            {
                if (!this.building.Position.Fogged(this.building.Map))
                    this.building.mindState.Active = true;
                this.building.GetLord()?.Notify_buildingDamaged(this.building, dinfo);
                if (dinfo.Def.ExternalViolenceFor((Thing)this.building))
                    GenClamor.DoClamor((Thing)this.building, 18f, ClamorDefOf.Harm);
                this.building.jobs.Notify_DamageTaken(dinfo);
            }
            if (homeFaction != null)
            {
                homeFaction.Notify_MemberTookDamage(this.building, dinfo);
                if (Current.ProgramState == ProgramState.Playing && homeFaction == Faction.OfPlayer && dinfo.Def.ExternalViolenceFor((Thing)this.building) && this.building.SbuildingedOrAnyParentSbuildinged)
                    this.building.MapHeld.dangerWatcher.Notify_ColonistHarmedExternally();
            }
            if (this.building.apparel != null && !dinfo.IgnoreArmor)
            {
                List<Apparel> wornApparel = this.building.apparel.WornApparel;
                for (int index = 0; index < wornApparel.Count; ++index)
                {
                    if (wornApparel[index].CheckPreAbsorbDamage(dinfo))
                    {
                        absorbed = true;
                        return;
                    }
                }
            }
            if (this.building.Sbuildinged)
            {
                this.building.stances.Notify_DamageTaken(dinfo);
                this.building.stances.stunner.Notify_DamageApplied(dinfo);
            }
            if (this.building.RaceProps.IsFlesh && dinfo.Def.ExternalViolenceFor((Thing)this.building))
            {
                if (dinfo.Instigator is building instigator)
                {
                    if (instigator.HostileTo((Thing)this.building))
                        this.building.relations.canGetRescuedThought = true;
                    if (this.building.RaceProps.Humanlike && instigator.RaceProps.Humanlike && this.building.needs.mood != null && (!instigator.HostileTo((Thing)this.building) || instigator.Faction == homeFaction && instigator.InMentalState))
                        this.building.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.HarmedMe, instigator);
                }
                TaleRecorder.RecordTale(TaleDefOf.Wounded, (object)this.building, (object)instigator, (object)dinfo.Weapon);
            }
            absorbed = false;
        }

        public void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            if (this.ShouldBeDead())
            {
                if (this.building.Destroyed)
                    return;
                this.building.Kill(new DamageInfo?(dinfo), (Hediff)null);
            }
            else
            {
                if (dinfo.Def.additionalHediffs != null && (dinfo.Def.applyAdditionalHediffsIfHuntingForFood || !(dinfo.Instigator is building instigator) || instigator.CurJob == null || instigator.CurJob.def != JobDefOf.PredatorHunt))
                {
                    List<DamageDefAdditionalHediff> additionalHediffs = dinfo.Def.additionalHediffs;
                    for (int index = 0; index < additionalHediffs.Count; ++index)
                    {
                        DamageDefAdditionalHediff additionalHediff = additionalHediffs[index];
                        if (additionalHediff.hediff != null)
                        {
                            float num = (double)additionalHediff.severityFixed <= 0.0 ? totalDamageDealt * additionalHediff.severityPerDamageDealt : additionalHediff.severityFixed;
                            if (additionalHediff.victimSeverityScalingByInvBodySize)
                                num *= 1f / this.building.BodySize;
                            if (additionalHediff.victimSeverityScaling != null)
                                num *= this.building.GetStatValue(additionalHediff.victimSeverityScaling);
                            if ((double)num >= 0.0)
                            {
                                Hediff hediff = HediffMaker.MakeHediff(additionalHediff.hediff, this.building);
                                hediff.Severity = num;
                                this.AddHediff(hediff, dinfo: new DamageInfo?(dinfo));
                                if (this.Dead)
                                    return;
                            }
                        }
                    }
                }
                for (int index = 0; index < this.hediffSet.hediffs.Count; ++index)
                    this.hediffSet.hediffs[index].Notify_buildingPostApplyDamage(dinfo, totalDamageDealt);
            }
        }

        public void RestorePart(BodyPartRecord part, Hediff diffException = null, bool checkStateChange = true)
        {
            if (part == null)
            {
                Log.Error("Tried to restore null body part.");
            }
            else
            {
                this.RestorePartRecursiveInt(part, diffException);
                this.hediffSet.DirtyCache();
                if (!checkStateChange)
                    return;
                this.CheckForStateChange(new DamageInfo?(), (Hediff)null);
            }
        }

        private void RestorePartRecursiveInt(BodyPartRecord part, Hediff diffException = null)
        {
            List<Hediff> hediffs = this.hediffSet.hediffs;
            for (int index = hediffs.Count - 1; index >= 0; --index)
            {
                Hediff hediff = hediffs[index];
                if (hediff.Part == part && hediff != diffException && !hediff.def.keepOnBodyPartRestoration)
                {
                    hediffs.RemoveAt(index);
                    hediff.PostRemoved();
                }
            }
            for (int index = 0; index < part.parts.Count; ++index)
                this.RestorePartRecursiveInt(part.parts[index], diffException);
        }

        public void CheckForStateChange(DamageInfo? dinfo, Hediff hediff)
        {
            if (this.Dead)
                return;
            if (this.ShouldBeDead())
            {
                if (this.building.Destroyed)
                    return;
                this.building.Kill(dinfo, hediff);
            }
            else if (!this.Downed)
            {
                if (this.ShouldBeDowned())
                {
                    if (!this.forceIncap && dinfo.HasValue && dinfo.Value.Def.ExternalViolenceFor((Thing)this.building) && !this.building.IsWildMan() && (this.building.Faction == null || !this.building.Faction.IsPlayer) && (this.building.HostFaction == null || !this.building.HostFaction.IsPlayer))
                    {
                        float num = !this.building.RaceProps.Animal ? (!this.building.RaceProps.IsMechanoid ? HealthTuning.DeathOnDownedChance_NonColonyHumanlikeFromPopulationIntentCurve.Evaluate(StorytellerUtilityPopulation.PopulationIntent) * Find.Storyteller.difficulty.enemyDeathOnDownedChanceFactor : 1f) : 0.5f;
                        if (Rand.Chance(num))
                        {
                            if (DebugViewSettings.logCauseOfDeath)
                                Log.Message("CauseOfDeath: chance on downed " + num.ToStringPercent());
                            this.building.Kill(dinfo, (Hediff)null);
                            return;
                        }
                    }
                    this.forceIncap = false;
                    this.MakeDowned(dinfo, hediff);
                }
                else
                {
                    if (this.capacities.CapableOf(buildingCapacityDefOf.Manipulation))
                        return;
                    if (this.building.carryTracker != null && this.building.carryTracker.CarriedThing != null && this.building.jobs != null && this.building.CurJob != null)
                        this.building.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    if (this.building.equipment == null || this.building.equipment.Primary == null)
                        return;
                    if (this.building.kindDef.destroyGearOnDrop)
                        this.building.equipment.DestroyEquipment(this.building.equipment.Primary);
                    else if (this.building.InContainerEnclosed)
                        this.building.equipment.TryTransferEquipmentToContainer(this.building.equipment.Primary, this.building.holdingOwner);
                    else if (this.building.SbuildingedOrAnyParentSbuildinged)
                        this.building.equipment.TryDropEquipment(this.building.equipment.Primary, out ThingWithComps _, this.building.PositionHeld);
                    else if (this.building.IsCaravanMember())
                    {
                        ThingWithComps primary = this.building.equipment.Primary;
                        this.building.equipment.Remove(primary);
                        if (this.building.inventory.innerContainer.TryAdd((Thing)primary, true))
                            return;
                        primary.Destroy(DestroyMode.Vanish);
                    }
                    else
                        this.building.equipment.DestroyEquipment(this.building.equipment.Primary);
                }
            }
            else
            {
                if (this.ShouldBeDowned())
                    return;
                this.MakeUndowned();
            }
        }

        private bool ShouldBeDowned() => this.InPainShock || !this.capacities.CanBeAwake || !this.capacities.CapableOf(buildingCapacityDefOf.Moving);

        private bool ShouldBeDead()
        {
            if (this.Dead)
                return true;
            for (int index = 0; index < this.hediffSet.hediffs.Count; ++index)
            {
                if (this.hediffSet.hediffs[index].CauseDeathNow())
                    return true;
            }
            if (this.ShouldBeDeadFromRequiredCapacity() != null)
                return true;
            if ((double)buildingCapacityUtility.CalculatePartEfficiency(this.hediffSet, this.building.RaceProps.body.corePart) <= 9.99999974737875E-05)
            {
                if (DebugViewSettings.logCauseOfDeath)
                    Log.Message("CauseOfDeath: zero efficiency of " + this.building.RaceProps.body.corePart.Label);
                return true;
            }
            return this.ShouldBeDeadFromLethalDamageThreshold();
        }

        public buildingCapacityDef ShouldBeDeadFromRequiredCapacity()
        {
            List<buildingCapacityDef> defsListForReading = DefDatabase<buildingCapacityDef>.AllDefsListForReading;
            for (int index = 0; index < defsListForReading.Count; ++index)
            {
                buildingCapacityDef capacity = defsListForReading[index];
                if ((this.building.RaceProps.IsFlesh ? (capacity.lethalFlesh ? 1 : 0) : (capacity.lethalMechanoids ? 1 : 0)) != 0 && !this.capacities.CapableOf(capacity))
                {
                    if (DebugViewSettings.logCauseOfDeath)
                        Log.Message("CauseOfDeath: no longer capable of " + capacity.defName);
                    return capacity;
                }
            }
            return (buildingCapacityDef)null;
        }

        public bool ShouldBeDeadFromLethalDamageThreshold()
        {
            float num = 0.0f;
            for (int index = 0; index < this.hediffSet.hediffs.Count; ++index)
            {
                if (this.hediffSet.hediffs[index] is Hediff_Injury)
                    num += this.hediffSet.hediffs[index].Severity;
            }
            bool flag = (double)num >= (double)this.LethalDamageThreshold;
            if (flag && DebugViewSettings.logCauseOfDeath)
                Log.Message("CauseOfDeath: lethal damage " + (object)num + " >= " + (object)this.LethalDamageThreshold);
            return flag;
        }

        public bool WouldLosePartAfterAddingHediff(HediffDef def, BodyPartRecord part, float severity)
        {
            Hediff hediff = HediffMaker.MakeHediff(def, this.building, part);
            hediff.Severity = severity;
            return this.CheckPredicateAfterAddingHediff(hediff, (Func<bool>)(() => this.hediffSet.PartIsMissing(part)));
        }

        public bool WouldDieAfterAddingHediff(Hediff hediff)
        {
            if (this.Dead)
                return true;
            int num = this.CheckPredicateAfterAddingHediff(hediff, new Func<bool>(this.ShouldBeDead)) ? 1 : 0;
            if (num == 0)
                return num != 0;
            if (!DebugViewSettings.logCauseOfDeath)
                return num != 0;
            Log.Message("CauseOfDeath: WouldDieAfterAddingHediff=true for " + (object)this.building.Name);
            return num != 0;
        }

        public bool WouldDieAfterAddingHediff(HediffDef def, BodyPartRecord part, float severity)
        {
            Hediff hediff = HediffMaker.MakeHediff(def, this.building, part);
            hediff.Severity = severity;
            return this.WouldDieAfterAddingHediff(hediff);
        }

        public bool WouldBeDownedAfterAddingHediff(Hediff hediff) => !this.Dead && this.CheckPredicateAfterAddingHediff(hediff, new Func<bool>(this.ShouldBeDowned));

        public bool WouldBeDownedAfterAddingHediff(HediffDef def, BodyPartRecord part, float severity)
        {
            Hediff hediff = HediffMaker.MakeHediff(def, this.building, part);
            hediff.Severity = severity;
            return this.WouldBeDownedAfterAddingHediff(hediff);
        }

        public void SetDead()
        {
            if (this.Dead)
                Log.Error(this.building.ToString() + " set dead while already dead.");
            this.healthState = BuildingHealthState.Dead;
        }

        private bool CheckPredicateAfterAddingHediff(Hediff hediff, Func<bool> pred)
        {
            HashSet<Hediff> missing = this.CalculateMissingPartHediffsFromInjury(hediff);
            this.hediffSet.hediffs.Add(hediff);
            if (missing != null)
                this.hediffSet.hediffs.AddRange((IEnumerable<Hediff>)missing);
            this.hediffSet.DirtyCache();
            int num = pred() ? 1 : 0;
            if (missing != null)
                this.hediffSet.hediffs.RemoveAll((Predicate<Hediff>)(x => missing.Contains(x)));
            this.hediffSet.hediffs.Remove(hediff);
            this.hediffSet.DirtyCache();
            return num != 0;
        }

        private HashSet<Hediff> CalculateMissingPartHediffsFromInjury(Hediff hediff)
        {
            HashSet<Hediff> missing = (HashSet<Hediff>)null;
            if (hediff.Part != null && hediff.Part != this.building.RaceProps.body.corePart && (double)hediff.Severity >= (double)this.hediffSet.GetPartHealth(hediff.Part))
            {
                missing = new HashSet<Hediff>();
                AddAllParts(hediff.Part);
            }
            return missing;

            void AddAllParts(BodyPartRecord part)
            {
                Hediff_MissingPart hediffMissingPart = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, this.building);
                hediffMissingPart.lastInjury = hediff.def;
                hediffMissingPart.Part = part;
                missing.Add((Hediff)hediffMissingPart);
                foreach (BodyPartRecord part1 in part.parts)
                    AddAllParts(part1);
            }
        }

        private void MakeDowned(DamageInfo? dinfo, Hediff hediff)
        {
            if (this.Downed)
            {
                Log.Error(this.building.ToString() + " tried to do MakeDowned while already downed.");
            }
            else
            {
                if (this.building.guilt != null && this.building.GetLord() != null && this.building.GetLord().LordJob != null && this.building.GetLord().LordJob.GuiltyOnDowned)
                    this.building.guilt.Notify_Guilty();
                this.healthState = BuildingHealthState.Down;
                buildingDiedOrDownedThoughtsUtility.TryGiveThoughts(this.building, dinfo, buildingDiedOrDownedThoughtsKind.Downed);
                if (this.building.InMentalState && this.building.MentalStateDef.recoverFromDowned)
                    this.building.mindState.mentalStateHandler.CurState.RecoverFromState();
                if (this.building.Sbuildinged)
                {
                    this.building.DropAndForbidEverything(true);
                    this.building.stances.CancelBusyStanceSoft();
                }
                this.building.ClearMind(true, clearMentalState: false);
                if (Current.ProgramState == ProgramState.Playing)
                {
                    Lord lord = this.building.GetLord();
                    if (lord != null && (lord.LordJob == null || lord.LordJob.RemoveDownedbuildings))
                        lord.Notify_buildingLost(this.building, buildingLostCondition.IncappedOrKilled, dinfo);
                }
                if (this.building.Drafted)
                    this.building.drafter.Drafted = false;
                PortraitsCache.SetDirty(this.building);
                GlobalTextureAtlasManager.TryMarkbuildingFrameSetDirty(this.building);
                if (this.building.SbuildingedOrAnyParentSbuildinged)
                    GenHostility.Notify_buildingLostForTutor(this.building, this.building.MapHeld);
                if (this.building.RaceProps.Humanlike && Current.ProgramState == ProgramState.Playing && this.building.SbuildingedOrAnyParentSbuildinged)
                {
                    if (this.building.HostileTo(Faction.OfPlayer))
                        LessonAutoActivator.TeachOpportunity(ConceptDefOf.Capturing, (Thing)this.building, OpportunityType.Important);
                    if (this.building.Faction == Faction.OfPlayer)
                        LessonAutoActivator.TeachOpportunity(ConceptDefOf.Rescuing, (Thing)this.building, OpportunityType.Critical);
                }
                DamageInfo damageInfo;
                if (dinfo.HasValue)
                {
                    damageInfo = dinfo.Value;
                    if (damageInfo.Instigator != null)
                    {
                        damageInfo = dinfo.Value;
                        if (damageInfo.Instigator is building instigator)
                            RecordsUtility.Notify_buildingDowned(this.building, instigator);
                    }
                }
                if (this.building.Sbuildinged)
                {
                    TaleDef downed = TaleDefOf.Downed;
                    object[] objArray = new object[3]
                    {
            (object) this.building,
            null,
            null
                    };
                    building building1;
                    if (!dinfo.HasValue)
                    {
                        building1 = (building)null;
                    }
                    else
                    {
                        damageInfo = dinfo.Value;
                        building1 = damageInfo.Instigator as building;
                    }
                    objArray[1] = (object)building1;
                    ThingDef thingDef;
                    if (!dinfo.HasValue)
                    {
                        thingDef = (ThingDef)null;
                    }
                    else
                    {
                        damageInfo = dinfo.Value;
                        thingDef = damageInfo.Weapon;
                    }
                    objArray[2] = (object)thingDef;
                    TaleRecorder.RecordTale(downed, objArray);
                    BattleLog battleLog = Find.BattleLog;
                    building building2 = this.building;
                    RulePackDef transitionDowned = RulePackDefOf.Transition_Downed;
                    building initiator;
                    if (!dinfo.HasValue)
                    {
                        initiator = (building)null;
                    }
                    else
                    {
                        damageInfo = dinfo.Value;
                        initiator = damageInfo.Instigator as building;
                    }
                    Hediff culpritHediff = hediff;
                    BodyPartRecord culpritTargetDef;
                    if (!dinfo.HasValue)
                    {
                        culpritTargetDef = (BodyPartRecord)null;
                    }
                    else
                    {
                        damageInfo = dinfo.Value;
                        culpritTargetDef = damageInfo.HitPart;
                    }
                    BattleLogEntry_StateTransition entry = new BattleLogEntry_StateTransition((Thing)building2, transitionDowned, initiator, culpritHediff, culpritTargetDef);
                    battleLog.Add((LogEntry)entry);
                }
                Find.Storyteller.Notify_buildingEvent(this.building, AdaptationEvent.Downed, dinfo);
            }
        }

        private void MakeUndowned()
        {
            if (!this.Downed)
            {
                Log.Error(this.building.ToString() + " tried to do MakeUndowned when already undowned.");
            }
            else
            {
                this.healthState = BuildingHealthState.Mobile;
                if (buildingUtility.ShouldSendNotificationAbout(this.building))
                    Messages.Message((string)"MessageNoLongerDowned".Translate((NamedArgument)this.building.LabelCap, (NamedArgument)(Thing)this.building), (LookTargets)(Thing)this.building, MessageTypeDefOf.PositiveEvent);
                if (this.building.Sbuildinged && !this.building.InBed())
                    this.building.jobs.EndCurrentJob(JobCondition.Incompletable);
                PortraitsCache.SetDirty(this.building);
                GlobalTextureAtlasManager.TryMarkbuildingFrameSetDirty(this.building);
                if (this.building.guest == null)
                    return;
                this.building.guest.Notify_buildingUndowned();
            }
        }

        public void NotifyPlayerOfKilled(DamageInfo? dinfo, Hediff hediff, Caravan caravan)
        {
            TaggedString taggedString1 = (TaggedString)"";
            TaggedString taggedString2 = !dinfo.HasValue ? (hediff == null ? "buildingDied".Translate((NamedArgument)this.building.LabelShortCap, this.building.Named("building")) : "buildingDiedBecauseOf".Translate((NamedArgument)this.building.LabelShortCap, (NamedArgument)hediff.def.LabelCap, this.building.Named("building"))) : dinfo.Value.Def.deathMessage.Formatted((NamedArgument)this.building.LabelShortCap, this.building.Named("building"));
            Quest quest = (Quest)null;
            if (this.building.IsBorrowedByAnyFaction())
            {
                foreach (QuestPart_LendColonistsToFaction colonistsToFaction in QuestUtility.GetAllQuestPartsOfType<QuestPart_LendColonistsToFaction>())
                {
                    if (colonistsToFaction.LentColonistsListForReading.Contains((Thing)this.building))
                    {
                        taggedString2 += "\n\n" + "LentColonistDied".Translate(this.building.Named("building"), colonistsToFaction.lendColonistsToFaction.Named("FACTION"));
                        quest = colonistsToFaction.quest;
                        break;
                    }
                }
            }
            TaggedString letter = taggedString2.AdjustedFor(this.building);
            if (this.building.Faction == Faction.OfPlayer)
            {
                TaggedString label = "Death".Translate() + ": " + this.building.LabelShortCap;
                if (caravan != null)
                    Messages.Message((string)"MessageCaravanDeathCorpseAddedToInventory".Translate(this.building.Named("building")), (LookTargets)(WorldObject)caravan, MessageTypeDefOf.buildingDeath);
                if (this.building.Ideo != null)
                {
                    foreach (Precept precept in this.building.Ideo.PreceptsListForReading)
                    {
                        if (!string.IsNullOrWhiteSpace(precept.def.extraTextbuildingDeathLetter))
                            letter += "\n\n" + precept.def.extraTextbuildingDeathLetter.Formatted(this.building.Named("building"));
                    }
                }
                if (this.building.Name != null && !this.building.Name.Numerical && this.building.RaceProps.Animal)
                    label += " (" + this.building.KindLabel + ")";
                this.building.relations.CheckAppendBondedAnimalDiedInfo(ref letter, ref label);
                Find.LetterStack.ReceiveLetter(label, letter, LetterDefOf.Death, (LookTargets)(Thing)this.building, quest: quest);
            }
            else
                Messages.Message((string)letter, (LookTargets)(Thing)this.building, MessageTypeDefOf.buildingDeath);
        }

        public void Notify_Resurrected()
        {
            this.healthState = BuildingHealthState.Mobile;
            this.hediffSet.hediffs.RemoveAll((Predicate<Hediff>)(x => x.def.everCurableByItem && x.TryGetComp<HediffComp_Immunizable>() != null));
            this.hediffSet.hediffs.RemoveAll((Predicate<Hediff>)(x => x.def.everCurableByItem && x is Hediff_Injury && !x.IsPermanent()));
            this.hediffSet.hediffs.RemoveAll((Predicate<Hediff>)(x =>
            {
                if (!x.def.everCurableByItem)
                    return false;
                if ((double)x.def.lethalSeverity >= 0.0)
                    return true;
                return x.def.stages != null && x.def.stages.Any<HediffStage>((Predicate<HediffStage>)(y => y.lifeThreatening));
            }));
            this.hediffSet.hediffs.RemoveAll((Predicate<Hediff>)(x => x.def.everCurableByItem && x is Hediff_Injury && x.IsPermanent() && (double)this.hediffSet.GetPartHealth(x.Part) <= 0.0));
            while (true)
            {
                Hediff_MissingPart hediffMissingPart = this.hediffSet.GetMissingPartsCommonAncestors().Where<Hediff_MissingPart>((Func<Hediff_MissingPart, bool>)(x => !this.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(x.Part))).FirstOrDefault<Hediff_MissingPart>();
                if (hediffMissingPart != null)
                    this.RestorePart(hediffMissingPart.Part, checkStateChange: false);
                else
                    break;
            }
            this.hediffSet.DirtyCache();
            if (this.ShouldBeDead())
                this.hediffSet.hediffs.RemoveAll((Predicate<Hediff>)(h => !h.def.keepOnBodyPartRestoration));
            this.Notify_HediffChanged((Hediff)null);
        }

        public void HealthTick()
        {
            if (this.Dead)
                return;
            for (int index = this.hediffSet.hediffs.Count - 1; index >= 0; --index)
            {
                Hediff hediff = this.hediffSet.hediffs[index];
                try
                {
                    hediff.Tick();
                    hediff.PostTick();
                }
                catch (Exception ex1)
                {
                    Log.Error("Exception ticking hediff " + hediff.ToStringSafe<Hediff>() + " for building " + this.building.ToStringSafe<building>() + ". Removing hediff... Exception: " + (object)ex1);
                    try
                    {
                        this.RemoveHediff(hediff);
                    }
                    catch (Exception ex2)
                    {
                        Log.Error("Error while removing hediff: " + (object)ex2);
                    }
                }
                if (this.Dead)
                    return;
            }
            bool flag1 = false;
            for (int index = this.hediffSet.hediffs.Count - 1; index >= 0; --index)
            {
                Hediff hediff = this.hediffSet.hediffs[index];
                if (hediff.ShouldRemove)
                {
                    this.hediffSet.hediffs.RemoveAt(index);
                    hediff.PostRemoved();
                    flag1 = true;
                }
            }
            if (flag1)
                this.Notify_HediffChanged((Hediff)null);
            if (this.Dead)
                return;
            this.immunity.ImmunityHandlerTick();
            if (this.building.RaceProps.IsFlesh && this.building.IsHashIntervalTick(600) && (this.building.needs.food == null || !this.building.needs.food.Starving))
            {
                bool flag2 = false;
                if (this.hediffSet.HasNaturallyHealingInjury())
                {
                    float num = 8f;
                    if (this.building.GetPosture() != buildingPosture.Standing)
                    {
                        num += 4f;
                        Building_Bed buildingBed = this.building.CurrentBed();
                        if (buildingBed != null)
                            num += buildingBed.def.building.bed_healPerDay;
                    }
                    foreach (Hediff hediff in this.hediffSet.hediffs)
                    {
                        HediffStage curStage = hediff.CurStage;
                        if (curStage != null && (double)curStage.naturalHealingFactor != -1.0)
                            num *= curStage.naturalHealingFactor;
                    }
                    this.hediffSet.GetHediffs<Hediff_Injury>().Where<Hediff_Injury>((Func<Hediff_Injury, bool>)(x => x.CanHealNaturally())).RandomElement<Hediff_Injury>().Heal((float)((double)num * (double)this.building.HealthScale * 0.00999999977648258) * this.building.GetStatValue(StatDefOf.InjuryHealingFactor));
                    flag2 = true;
                }
                if (this.hediffSet.HasTendedAndHealingInjury() && (this.building.needs.food == null || !this.building.needs.food.Starving))
                {
                    Hediff_Injury hd = this.hediffSet.GetHediffs<Hediff_Injury>().Where<Hediff_Injury>((Func<Hediff_Injury, bool>)(x => x.CanHealFromTending())).RandomElement<Hediff_Injury>();
                    hd.Heal((float)(8.0 * (double)GenMath.LerpDouble(0.0f, 1f, 0.5f, 1.5f, Mathf.Clamp01(hd.TryGetComp<HediffComp_TendDuration>().tendQuality)) * (double)this.building.HealthScale * 0.00999999977648258) * this.building.GetStatValue(StatDefOf.InjuryHealingFactor));
                    flag2 = true;
                }
                if (flag2 && !this.HasHediffsNeedingTendByPlayer() && !HealthAIUtility.ShouldSeekMedicalRest(this.building) && !this.hediffSet.HasTendedAndHealingInjury() && buildingUtility.ShouldSendNotificationAbout(this.building))
                    Messages.Message((string)"MessageFullyHealed".Translate((NamedArgument)this.building.LabelCap, (NamedArgument)(Thing)this.building), (LookTargets)(Thing)this.building, MessageTypeDefOf.PositiveEvent);
            }
            if (this.building.RaceProps.IsFlesh && (double)this.hediffSet.BleedRateTotal >= 0.100000001490116)
            {
                float num = this.hediffSet.BleedRateTotal * this.building.BodySize;
                if ((double)Rand.Value < (this.building.GetPosture() != buildingPosture.Standing ? (double)(num * 0.0004f) : (double)(num * 0.004f)))
                    this.DropBloodFilth();
            }
            if (!this.building.IsHashIntervalTick(60))
                return;
            List<HediffGiverSetDef> hediffGiverSets = this.building.RaceProps.hediffGiverSets;
            if (hediffGiverSets != null)
            {
                for (int index1 = 0; index1 < hediffGiverSets.Count; ++index1)
                {
                    List<HediffGiver> hediffGivers = hediffGiverSets[index1].hediffGivers;
                    for (int index2 = 0; index2 < hediffGivers.Count; ++index2)
                    {
                        hediffGivers[index2].OnIntervalPassed(this.building, (Hediff)null);
                        if (this.building.Dead)
                            return;
                    }
                }
            }
            if (this.building.story == null)
                return;
            List<Trait> allTraits = this.building.story.traits.allTraits;
            for (int index = 0; index < allTraits.Count; ++index)
            {
                TraitDegreeData currentData = allTraits[index].CurrentData;
                if ((double)currentData.randomDiseaseMtbDays > 0.0 && Rand.MTBEventOccurs(currentData.randomDiseaseMtbDays, 60000f, 60f))
                {
                    BiomeDef biome = this.building.Tile == -1 ? DefDatabase<BiomeDef>.GetRandom() : Find.WorldGrid[this.building.Tile].biome;
                    IncidentDef incidentDef = DefDatabase<IncidentDef>.AllDefs.Where<IncidentDef>((Func<IncidentDef, bool>)(d => d.category == IncidentCategoryDefOf.DiseaseHuman)).RandomElementByWeightWithFallback<IncidentDef>((Func<IncidentDef, float>)(d => biome.CommonalityOfDisease(d)));
                    if (incidentDef != null)
                    {
                        string blockedInfo;
                        List<building> buildings = ((IncidentWorker_Disease)incidentDef.Worker).ApplyTobuildings(Gen.YieldSingle<building>(this.building), out blockedInfo);
                        if (buildingUtility.ShouldSendNotificationAbout(this.building))
                        {
                            if (buildings.Contains(this.building))
                                Find.LetterStack.ReceiveLetter("LetterLabelTraitDisease".Translate((NamedArgument)incidentDef.diseaseIncident.label), "LetterTraitDisease".Translate((NamedArgument)this.building.LabelCap, (NamedArgument)incidentDef.diseaseIncident.label, this.building.Named("building")).AdjustedFor(this.building), LetterDefOf.NegativeEvent, (LookTargets)(Thing)this.building);
                            else if (!blockedInfo.NullOrEmpty())
                                Messages.Message(blockedInfo, (LookTargets)(Thing)this.building, MessageTypeDefOf.NeutralEvent);
                        }
                    }
                }
            }
        }

        public bool HasHediffsNeedingTend(bool forAlert = false) => this.hediffSet.HasTendableHediff(forAlert);

        public bool HasHediffsNeedingTendByPlayer(bool forAlert = false)
        {
            if (this.HasHediffsNeedingTend(forAlert))
            {
                if (this.building.NonHumanlikeOrWildMan())
                {
                    if (this.building.Faction == Faction.OfPlayer)
                        return true;
                    Building_Bed buildingBed = this.building.CurrentBed();
                    if (buildingBed != null && buildingBed.Faction == Faction.OfPlayer)
                        return true;
                }
                else if (this.building.Faction == Faction.OfPlayer && this.building.HostFaction == null || this.building.HostFaction == Faction.OfPlayer)
                    return true;
            }
            return false;
        }

        public void DropBloodFilth()
        {
            if (!this.building.Sbuildinged && !(this.building.ParentHolder is building_CarryTracker) || !this.building.SbuildingedOrAnyParentSbuildinged || this.building.RaceProps.BloodDef == null)
                return;
            FilthMaker.TryMakeFilth(this.building.PositionHeld, this.building.MapHeld, this.building.RaceProps.BloodDef, this.building.LabelIndefinite());
        }

        public IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Hediff hediff in this.hediffSet.hediffs)
            {
                IEnumerable<Gizmo> gizmos = hediff.GetGizmos();
                if (gizmos != null)
                {
                    foreach (Gizmo gizmo in gizmos)
                        yield return gizmo;
                }
            }
        }
    }
}
}
