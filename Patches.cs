using HarmonyLib;
using Photon.Realtime;
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
            AltNetIk.Logger.Msg("Applying patches...");

            // Patches stolen from Loukylor, knah, ImTiara and Requi
            AltNetIk.Instance.HarmonyInstance.Patch(typeof(VRCAvatarManager).GetMethods().First(mb => mb.Name.StartsWith("Method_Private_Boolean_GameObject_String_Single_")), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnAvatarInit), BindingFlags.NonPublic | BindingFlags.Static)));
            AltNetIk.Instance.HarmonyInstance.Patch(typeof(VRCPlayer).GetMethods().First(mb => mb.Name.StartsWith("Awake")), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnPlayerAwake), BindingFlags.NonPublic | BindingFlags.Static)));
            AltNetIk.Instance.HarmonyInstance.Patch(typeof(NetworkManager).GetMethod("OnLeftRoom"), new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnRoomLeave), BindingFlags.NonPublic | BindingFlags.Static)));
            AltNetIk.Instance.HarmonyInstance.Patch(typeof(PipelineManager).GetMethod(nameof(PipelineManager.Start)), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnAvatarChanged), BindingFlags.NonPublic | BindingFlags.Static)));
            AltNetIk.Instance.HarmonyInstance.Patch(typeof(VRC.UI.Elements.QuickMenu).GetMethod(nameof(VRC.UI.Elements.QuickMenu.OnEnable)), new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnQuickMenuOpen), BindingFlags.NonPublic | BindingFlags.Static)));
            AltNetIk.Instance.HarmonyInstance.Patch(typeof(LoadBalancingClient).GetMethods().Single(m => m.Name.StartsWith("Method_Private_Void_OperationResponse_") && XrefUtils.CheckUsing(m, "OnCreatedRoom")), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnPhotonInstanceChanged), BindingFlags.NonPublic | BindingFlags.Static)));

            var field0 = NetworkManager.field_Internal_Static_NetworkManager_0.field_Internal_VRCEventDelegate_1_Player_0;
            var field1 = NetworkManager.field_Internal_Static_NetworkManager_0.field_Internal_VRCEventDelegate_1_Player_1;
            field0.field_Private_HashSet_1_UnityAction_1_T_0.Add(new Action<VRC.Player>((player) => { if (player != null) AltNetIk.Instance.OnPlayerJoined(player); }));
            field1.field_Private_HashSet_1_UnityAction_1_T_0.Add(new Action<VRC.Player>((player) => { if (player != null) AltNetIk.Instance.OnPlayerLeft(player); }));
        }

        private static void OnAvatarInit(VRCAvatarManager __instance)
        {
            AltNetIk.Instance.OnAvatarInit(__instance);
        }

        private static void OnPlayerAwake(VRCPlayer __instance)
        {
            __instance.Method_Public_add_Void_OnAvatarIsReady_0(new Action(()
                => OnAvatarChange(__instance.prop_VRCAvatarManager_0, __instance.field_Private_ApiAvatar_0, __instance.field_Internal_GameObject_0))
            );
        }

        private static void OnAvatarChange(VRCAvatarManager manager, ApiAvatar apiAvatar, GameObject avatar)
        {
            if (manager == null || apiAvatar == null || avatar == null)
                return;

            AltNetIk.Instance.OnAvatarChange(manager);
        }

        private static void OnAvatarChanged(PipelineManager __instance)
        {
            var avatarManager = VRCPlayer.field_Internal_Static_VRCPlayer_0?.prop_VRCAvatarManager_0;
            if (avatarManager == null)
                return;

            if (__instance.gameObject.Pointer != VRCPlayer.field_Internal_Static_VRCPlayer_0.prop_VRCAvatarManager_0.field_Private_GameObject_0.Pointer)
                return;

            AltNetIk.Instance.OnAvatarChange(avatarManager);
        }

        private static void OnPhotonInstanceChanged()
        {
            AltNetIk.Instance.OnPhotonInstanceChanged();
        }

        private static void OnRoomLeave()
        {
            AltNetIk.Instance.ResetInstance();
        }

        private static void OnQuickMenuOpen()
        {
            Buttons.UpdateAllButtons();
        }
    }
}