using VRC;
using VRC.Core;
using HarmonyLib;
using System.Reflection;
using System.Linq;
using System;
using UnityEngine;

namespace AltNetIk
{
    public class Patches
    {
        public static void DoPatches()
        {
            AltNetIk.Logger.Msg("Applying patches...");

            var harmony = new HarmonyLib.Harmony("AltNetIk");

            // Patches stolen from Loukylor, knah and Requi
            harmony.Patch(typeof(VRCAvatarManager).GetMethods().First(mb => mb.Name.StartsWith("Method_Private_Boolean_GameObject_String_Single_")), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnAvatarChange), BindingFlags.NonPublic | BindingFlags.Static)));
            harmony.Patch(typeof(VRCPlayer).GetMethods().First(mb => mb.Name.StartsWith("Awake")), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnPlayerAwake), BindingFlags.NonPublic | BindingFlags.Static)));
            harmony.Patch(typeof(RoomManager).GetMethod("Method_Public_Static_Boolean_ApiWorld_ApiWorldInstance_String_Int32_0"), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnInstanceChange), BindingFlags.NonPublic | BindingFlags.Static)));
            harmony.Patch(typeof(NetworkManager).GetMethod("OnLeftRoom"), new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnRoomLeave), BindingFlags.NonPublic | BindingFlags.Static)));
            harmony.Patch(typeof(VRC.UI.Elements.QuickMenu).GetMethod(nameof(VRC.UI.Elements.QuickMenu.OnEnable)), new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnQuickMenuOpen), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void OnAvatarChange(VRCAvatarManager __instance)
        {
            AltNetIk.Instance.OnAvatarInit(__instance, __instance.prop_GameObject_0);
        }

        private static void OnPlayerAwake(VRCPlayer __instance)
        {
            __instance.Method_Public_add_Void_OnAvatarIsReady_0(new Action(()
                => OnAvatarInstantiate(__instance.prop_VRCAvatarManager_0, __instance.field_Private_ApiAvatar_0, __instance.field_Internal_GameObject_0))
            );
        }

        private static void OnAvatarInstantiate(VRCAvatarManager manager, ApiAvatar apiAvatar, GameObject avatar)
        {
            if (manager == null || apiAvatar == null || avatar == null)
                return;

            AltNetIk.Instance.OnAvatarChange(manager, apiAvatar, avatar);
        }

        private static void OnInstanceChange(ApiWorld __0, ApiWorldInstance __1)
        {
            if (__0 == null || __1 == null)
                return;

            AltNetIk.Instance.OnInstanceChanged(__0, __1);
        }

        private static void OnRoomLeave()
        {
            AltNetIk.Instance.ResetInstance();
        }

        private static void OnQuickMenuOpen()
        {
            AltNetIk.UpdateAllButtons();
        }
    }        
}