﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimWorld
{
   public class CompProperties_ScaffoldConverter : CompProperties
    {
        public CompProperties_ScaffoldConverter()
        {
            compClass = typeof(CompScaffoldConverter);
        }
    }
}
