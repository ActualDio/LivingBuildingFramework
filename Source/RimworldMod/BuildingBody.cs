using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld.Planet;
using UnityEngine;
using Verse.AI.Group;

namespace RimWorld
{
    public class BuildingBody
    {
        public CompBuildingCore heart = null;
        public CompScaffoldConverter scaffoldConverter = null;

        public HashSet<Thing> bodyParts = new HashSet<Thing>();
        public HashSet<CompNutritionConsumer> consumers = new HashSet<CompNutritionConsumer>();
        public HashSet<CompNutritionStore> stores = new HashSet<CompNutritionStore>();
        public HashSet<CompNutritionSource> source = new HashSet<CompNutritionSource>();
        private int maxDepth = 5;
        public float currentNutrition = 0;
        public float nutritionCapacity = 0;
        public float passiveConsumption = 0;
        public float nutritionGen = 0;
        public float tempHunger = 0;

        public virtual string GetSpecies()
        {
            if (heart != null)
            {
                return heart.Props.species;
            }
            return null;
        }
        public virtual float GetConversionNutritionCost()
        {
            return 15f;
        }

        public virtual void Register(CompBuildingCore _heart)
        {
            heart = _heart;
            heart.body = this;
        }

        public virtual void Register(CompBuildingBodyPart comp)
        {
            bodyParts.Add(comp.parent);
            comp.body = this;
        }
        public virtual void Register(CompNutrition comp)
        {
            if (comp is CompNutritionConsumer)
            {
                consumers.Add((CompNutritionConsumer)comp);
                passiveConsumption += ((CompNutritionConsumer)comp).getConsumptionPerPulse();
            }
            if (comp is CompNutritionSource)
            {
                source.Add((CompNutritionSource)comp);
                nutritionGen += ((CompNutritionSource)comp).getNutritionPerPulse();
            }
            if (comp is CompNutritionStore)
            {
                stores.Add((CompNutritionStore)comp);
                nutritionCapacity += ((CompNutritionStore)comp).getNutrientCapacity();
                currentNutrition += ((CompNutritionStore)comp).getCurrentNutrition();
            }
            comp.body = this;
        }

        public virtual void DeRegister(CompBuildingBodyPart comp)
        {
            bodyParts.Remove(comp.parent);
        }
        public virtual void DeRegister(CompNutrition comp)
        {
            if (comp is CompNutritionConsumer)
            {
                consumers.Remove((CompNutritionConsumer)comp);
                passiveConsumption -= ((CompNutritionConsumer)comp).getConsumptionPerPulse();
            }
            if (comp is CompNutritionStore)
            {
                stores.Remove((CompNutritionStore)comp);
                nutritionCapacity -= ((CompNutritionStore)comp).getNutrientCapacity();
                currentNutrition -= ((CompNutritionStore)comp).getNutrientCapacity();
            }
            if (comp is CompNutritionSource)
            {
                source.Add((CompNutritionSource)comp);
                nutritionGen += ((CompNutritionSource)comp).getNutritionPerPulse();
            }
        }

        public virtual void UpdateNutritionGeneration()
        {
            nutritionGen = 0;
            foreach(CompNutritionSource c in source)
            {
                nutritionGen += c.getNutritionPerPulse();
            }
        }
        public void UpdateNutritionCapacity()
        {
            nutritionCapacity = 0;
            foreach (CompNutritionStore c in stores)
            {
                nutritionCapacity += c.getNutrientCapacity();
            }
        }
        public virtual void UpdateCurrentNutrition()
        {
            currentNutrition = 0;
            foreach (CompNutritionStore c in stores)
            {
                currentNutrition += c.getCurrentNutrition();
            }
        }
        public virtual void UpdatePassiveConsumption()
        {
            passiveConsumption = 1*bodyParts.Count + 150;
            foreach (CompNutritionConsumer c in consumers)
            {
                passiveConsumption += c.getConsumptionPerPulse();
            }
        }
        public virtual bool RequestNutrition(float qty)
        {
            if (qty > currentNutrition)
            {
                return false;
            }

            ExtractNutrition(stores, qty, 0);
            currentNutrition -= qty;
            return true;
        }

        public virtual void RunNutrition()
        {
            if (heart == null)
            {
                return;
            }
            if (heart.hungerDuration > 200)
            {

            }
            UpdatePassiveConsumption();
            UpdateNutritionGeneration();
            UpdateCurrentNutrition();
            float net = nutritionGen - passiveConsumption - tempHunger;
            net = net / (60000f/120f);
            if (net > 0)
            {
                float toStore = net * 0.5f;
                float leftover = 0;
                if ((nutritionCapacity - currentNutrition) <= 0)
                {
                    leftover = toStore;
                } 
                else if (toStore >= (nutritionCapacity - currentNutrition)) 
                {
                    leftover = toStore - (nutritionCapacity - currentNutrition);
                    currentNutrition = nutritionCapacity;
                    foreach (CompNutritionStore store in stores)
                    {
                        store.currentNutrition = store.getNutrientCapacity();
                    }
                } else
                {
                    leftover = StoreNutrition(stores, toStore, 0);
                }
            }
            if (net < 0)
            {
                float deficit = 0;
                net = net * -1;
                if (net > currentNutrition)
                {
                    deficit = net - currentNutrition;
                    currentNutrition = 0;
                    foreach (CompNutritionStore store in stores)
                    {
                        store.currentNutrition = 0;
                    }
                    if (deficit > 0 && bodyParts.Count > 0)
                    {
                        heart.hungerDuration++;
                    } 
                }
                else
                {
                    ExtractNutrition(stores, net, 0);
                }
            }
            tempHunger = 0;
        }

        public virtual float StoreNutrition(HashSet<CompNutritionStore> _stores, float toStore, int depth)
        {
            if (_stores.Count == 0 || depth > maxDepth)
            {
                return toStore;
            }
            float leftOver = 0;
            HashSet<CompNutritionStore> retainCapactiy = new HashSet<CompNutritionStore>();
            float storeEach = toStore/_stores.Count;
            foreach (CompNutritionStore s in _stores)
            {
                leftOver += s.storeNutrition(storeEach);
                if (s.currentNutrition < s.getNutrientCapacity())
                {
                    retainCapactiy.Add(s);
                }
            }

            if (leftOver <= 0)
            {
                return 0;
            } 
            else
            {
                return StoreNutrition(retainCapactiy, leftOver, depth+1);
            }
        }
        public virtual float ExtractNutrition(HashSet<CompNutritionStore> _stores, float toExtract, int depth)
        {
            if (_stores.Count <= 0 || depth > maxDepth)
            {
                return toExtract;
            }
            HashSet<CompNutritionStore> retainNutrition = new HashSet<CompNutritionStore>();
            float localExtract = toExtract/_stores.Count;
            float remainingHunger = 0;
            foreach (CompNutritionStore s in _stores)
            {
                remainingHunger += s.consumeNutrition(localExtract);
                if (s.currentNutrition > 0)
                {
                    retainNutrition.Add(s);
                }
            }
            if (remainingHunger <= 0 || retainNutrition.Count == 0)
            {
                return 0;
            } else
            {
                return ExtractNutrition(retainNutrition, remainingHunger, depth+1);
            }
        }
    }
}