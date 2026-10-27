using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BigSmallWarcasketExemption
{
    // Big and Small's "cannot wear armor / clothing / anything" genes decide armor vs clothing by name, so a
    // warcasket's helmet counts as armor and its body and shoulders as clothing. VFE Pirates puts all three on
    // without asking, and B&S's next cache refresh strips whichever pieces the gene forbids, leaving the pawn
    // half-entombed. This lets warcasket pieces past those three flags only: B&S's layer, body-part and tag
    // filters still apply as normal.
    //
    // Both B&S and VFE Pirates are resolved by name, so an update to either can only make this skip itself.
    [StaticConstructorOnStartup]
    public static class WarcasketExemption
    {
        private static readonly HashSet<ThingDef> warcasketDefs = new HashSet<ThingDef>();
        private static readonly HashSet<ApparelProperties> warcasketApparel = new HashSet<ApparelProperties>();

        private static AccessTools.FieldRef<object, bool> noArmor;
        private static AccessTools.FieldRef<object, bool> noClothes;
        private static AccessTools.FieldRef<object, bool> absolutelyNothing;
        private static Func<object, object> memberwiseClone;
        private static MethodInfo canWearDef;
        private static MethodInfo canWearProps;

        static WarcasketExemption()
        {
            Type restrictions = AccessTools.TypeByName("BigAndSmall.ApparelRestrictions");
            Type warcasket = AccessTools.TypeByName("VFEPirates.Apparel_Warcasket");
            if (restrictions == null || warcasket == null)
            {
                return;
            }
            canWearDef = AccessTools.Method(restrictions, "CanWear", new[] { typeof(ThingDef) });
            canWearProps = restrictions.GetMethods(AccessTools.all).FirstOrDefault(m => m.Name == "CanWear"
                && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(ApparelProperties));
            if (canWearDef == null || canWearProps == null || canWearDef.ReturnType != typeof(string))
            {
                Log.Warning("[B&S Warcasket Exemption] Big and Small's ApparelRestrictions.CanWear changed; warcaskets are not exempted.");
                return;
            }
            noArmor = AccessTools.FieldRefAccess<bool>(restrictions, "noArmor");
            noClothes = AccessTools.FieldRefAccess<bool>(restrictions, "noClothes");
            absolutelyNothing = AccessTools.FieldRefAccess<bool>(restrictions, "absolutelyNothing");
            memberwiseClone = AccessTools.MethodDelegate<Func<object, object>>(AccessTools.Method(typeof(object), "MemberwiseClone"));

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.apparel != null && def.thingClass != null && warcasket.IsAssignableFrom(def.thingClass))
                {
                    warcasketDefs.Add(def);
                    warcasketApparel.Add(def.apparel);
                }
            }

            Harmony harmony = new Harmony("DanZinagri.BigSmall.WarcasketExemption");
            harmony.Patch(canWearDef, postfix: new HarmonyMethod(typeof(WarcasketExemption), nameof(CanWearDef_Postfix)));
            harmony.Patch(canWearProps, postfix: new HarmonyMethod(typeof(WarcasketExemption), nameof(CanWearProps_Postfix)));
        }

        // A refusal for a warcasket piece is re-asked of a copy of the restrictions with the three flags cleared,
        // so the shared (cached, fused) restrictions object is never written to. The copy has no flags set, so
        // these postfixes do nothing when it runs.
        private static bool AnyFlag(object restrictions)
        {
            return noArmor(restrictions) || noClothes(restrictions) || absolutelyNothing(restrictions);
        }

        private static object WithoutFlags(object restrictions)
        {
            object copy = memberwiseClone(restrictions);
            noArmor(copy) = false;
            noClothes(copy) = false;
            absolutelyNothing(copy) = false;
            return copy;
        }

        public static void CanWearDef_Postfix(object __instance, ThingDef thingDef, ref string __result)
        {
            if (__result == null || thingDef == null || !warcasketDefs.Contains(thingDef) || !AnyFlag(__instance))
            {
                return;
            }
            __result = (string)canWearDef.Invoke(WithoutFlags(__instance), new object[] { thingDef });
        }

        // The out FilterResult is left as the original call set it: B&S only reads it from inside
        // CanWear(ThingDef), which the postfix above recomputes as a whole.
        public static void CanWearProps_Postfix(object __instance, ApparelProperties apparel, ref string __result)
        {
            if (__result == null || apparel == null || !warcasketApparel.Contains(apparel) || !AnyFlag(__instance))
            {
                return;
            }
            __result = (string)canWearProps.Invoke(WithoutFlags(__instance), new object[] { apparel, null });
        }
    }
}
