using HarmonyLib;
using ReMod.Core;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRC;
using VRC.Core;

namespace AltNetIk
{
    public class Patches
    {
        public static void DoPatches()
        {
            AltNetIk.Instance.Logger.Msg("Applying patches...");

            var harmony = new HarmonyLib.Harmony("AltNetIk");

            // Patches stolen from Loukylor, knah and Requi
            harmony.Patch(typeof(VRCAvatarManager).GetMethods().First(mb => mb.Name.StartsWith("Method_Private_Boolean_GameObject_String_Single_")), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnAvatarInit), BindingFlags.NonPublic | BindingFlags.Static)));
            harmony.Patch(typeof(PipelineManager).GetMethod(nameof(PipelineManager.Start)), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnAvatarChanged), BindingFlags.NonPublic | BindingFlags.Static)));
            harmony.Patch(typeof(VRC.UI.Elements.QuickMenu).GetMethod(nameof(VRC.UI.Elements.QuickMenu.OnEnable)), new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnQuickMenuOpen), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void OnAvatarInit(VRCAvatarManager __instance)
        {
            AltNetIk.Instance.OnAvatarInit(__instance, __instance.prop_GameObject_0);
        }

        private static void OnAvatarChanged(PipelineManager __instance)
        {
            var avatarManager = VRCPlayer.field_Internal_Static_VRCPlayer_0?.prop_VRCAvatarManager_0;
            if (avatarManager == null)
                return;

            if (__instance.gameObject.Pointer != VRCPlayer.field_Internal_Static_VRCPlayer_0.prop_VRCAvatarManager_0.field_Private_GameObject_0.Pointer)
                return;

            AltNetIk.Instance.OnAvatarIsReady(avatarManager.field_Private_VRCPlayer_0);
        }

        private static void OnQuickMenuOpen()
        {
            AltNetIk.Instance.UpdateAllButtons();
        }
    }
}