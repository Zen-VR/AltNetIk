using HarmonyLib;
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
            harmony.Patch(typeof(VRCVrIkController).GetMethod("Method_Private_Void_PDM_8"), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnCalibrate), BindingFlags.NonPublic | BindingFlags.Static)));
            harmony.Patch(typeof(VRC.UI.Elements.QuickMenu).GetMethod(nameof(VRC.UI.Elements.QuickMenu.OnEnable)), new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnQuickMenuOpen), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void OnAvatarInit(VRCAvatarManager __instance)
        {
            AltNetIk.Instance.OnAvatarInit(__instance, __instance.prop_GameObject_0);
        }

        private static void OnCalibrate()
        {
            var avatarManager = AltNetIk.Instance.senderPlayerData?.playerAvatarManager;
            if (avatarManager != null)
                AltNetIk.Instance.OnAvatarChange(avatarManager);
        }

        private static void OnQuickMenuOpen()
        {
            AltNetIk.Instance.UpdateAllButtons();
        }
    }
}