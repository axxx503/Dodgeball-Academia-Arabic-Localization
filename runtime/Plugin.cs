using System;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
namespace AcademiaArabic
{
    [BepInPlugin("local.dodgeballacademia.arabic", "Dodgeball Academia Arabic", "0.6.0")]
    public sealed class Plugin:BasePlugin
    {
        static Plugin instance;
        static int successful,failed;
        public override void Load()
        {
            instance=this;
            var target=AccessTools.Method(typeof(TextRenderer),"BuildSection",new[]{typeof(TextRenderer.Section),typeof(FontFamily),typeof(float),typeof(UnityEngine.Color),typeof(bool)});
            if(target==null)throw new MissingMethodException("This game's TextRenderer.BuildSection signature is unavailable");
            new Harmony("local.dodgeballacademia.arabic").Patch(target,
                prefix:new HarmonyMethod(typeof(Plugin),nameof(BeforeSection)),
                postfix:new HarmonyMethod(typeof(Plugin),nameof(AfterSection)),
                finalizer:new HarmonyMethod(typeof(Plugin),nameof(FinalizeSection)));
            Log.LogInfo("Arabic renderer candidate loaded; local validation pending. Original logical section and glyph list order retained.");
        }
        static void BeforeSection(TextRenderer __instance,TextRenderer.Section section,FontFamily currentFont,float currentSize,out SectionState __state)
        {
            __state=null;
            try{__state=NativeSectionAdapter.Prepare(__instance,section,currentFont,currentSize);}
            catch(Exception error){Failure("prepare",error);}
        }
        static void AfterSection(TextRenderer __instance,TextRenderer.BuildData __result,SectionState __state)
        {
            if(__state==null)return;
            try
            {
                var projection=NativeSectionAdapter.Inspect(__instance,__result,__state);
                NativeGeometryTransaction.Create(projection,__state).Commit();
                successful++;
                if(successful<=8||successful%100==0)
                    instance.Log.LogInfo("ARABIC_LAYOUT_OK count="+successful+" chars="+__state.Logical.Length+" lines="+projection.Lines.Count);
            }
            catch(Exception error){Failure("geometry",error);}
            finally{NativeSectionAdapter.Restore(__state);}
        }
        static Exception FinalizeSection(Exception __exception,SectionState __state)
        {
            NativeSectionAdapter.Restore(__state);
            // Do not swallow an exception raised by the original game method.
            return __exception;
        }
        static void Failure(string stage,Exception error)
        {
            failed++;
            if(failed<=20||failed%100==0)instance.Log.LogError("ARABIC_LAYOUT_FAILED stage="+stage+" count="+failed+" "+error);
        }
    }
}
