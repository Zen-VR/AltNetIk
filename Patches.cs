using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Linq;
using System.Reflection;
using UnhollowerRuntimeLib.XrefScans;
using UnityEngine;
using VRC.Core;
using VRC.Networking.Pose;
using Logger = UnityEngine.Logger;

namespace AltNetIk
{
    public class Patches
    {
        public static void DoPatches()
        {
            AltNetIk.Logger.Msg("Applying patches...");

            // Patches stolen from Loukylor, knah, ImTiara and Requi
            AltNetIk.Logger.Msg("1");
            AltNetIk.Instance.HarmonyInstance.Patch(typeof(VRCAvatarManager).GetMethods().First(mb => mb.Name.StartsWith("Method_Private_Boolean_GameObject_String_Single_")), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnAvatarInit), BindingFlags.NonPublic | BindingFlags.Static)));
            AltNetIk.Logger.Msg("2");
            AltNetIk.Instance.HarmonyInstance.Patch(typeof(NetworkManager).GetMethod("OnLeftRoom"), new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnRoomLeave), BindingFlags.NonPublic | BindingFlags.Static)));
            AltNetIk.Logger.Msg("3");
            AltNetIk.Instance.HarmonyInstance.Patch(typeof(PipelineManager).GetMethod(nameof(PipelineManager.Start)), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnAvatarChanged), BindingFlags.NonPublic | BindingFlags.Static)));
            AltNetIk.Logger.Msg("4");
            AltNetIk.Instance.HarmonyInstance.Patch(typeof(PoseRemoteUpdate).GetMethod(nameof(PoseRemoteUpdate.Start)), new HarmonyMethod(typeof(Patches).GetMethod(nameof(Av3PoseUpdate), BindingFlags.NonPublic | BindingFlags.Static)));
            AltNetIk.Logger.Msg("5");
            //AltNetIk.Instance.HarmonyInstance.Patch(typeof(LoadBalancingClient).GetMethods().Single(m => m.Name.StartsWith("Method_Private_Void_OperationResponse_") && CheckUsing(m, "OnCreatedRoom")), null, new HarmonyMethod(typeof(Patches).GetMethod(nameof(OnPhotonInstanceChanged), BindingFlags.NonPublic | BindingFlags.Static)));
            //AltNetIk.Logger.Msg("5");

            var field0 = NetworkManager.field_Internal_Static_NetworkManager_0.field_Internal_VRCEventDelegate_1_Player_0;
            var field1 = NetworkManager.field_Internal_Static_NetworkManager_0.field_Internal_VRCEventDelegate_1_Player_2;
            field0.field_Private_HashSet_1_UnityAction_1_T_0.Add(new Action<VRC.Player>((player) => { if (player != null) AltNetIk.Instance.OnPlayerJoined(player); }));
            field1.field_Private_HashSet_1_UnityAction_1_T_0.Add(new Action<VRC.Player>((player) => { if (player != null) AltNetIk.Instance.OnPlayerLeft(player); }));

            AltNetIk.Logger.Msg("Patches done");
        }

        private static void OnAvatarInit(VRCAvatarManager __instance)
        {
            AltNetIk.Instance.OnAvatarInit(__instance);
        }

        //private static void OnPlayerAwake(VRCPlayer __instance)
        //{
        //    __instance.Method_Public_add_Void_Action_0(new Action(()
        //        => OnAvatarChange(__instance.prop_VRCAvatarManager_0, __instance.field_Private_ApiAvatar_0, __instance.field_Internal_GameObject_0))
        //    );
        //}

        private static void OnAvatarChange(VRCAvatarManager manager, ApiAvatar apiAvatar, GameObject avatar)
        {
            if (manager == null || apiAvatar == null || avatar == null)
                return;

            AltNetIk.Logger.Msg("new patch hit");

            AltNetIk.Instance.OnAvatarChange(manager);
        }

        private static void OnAvatarChanged(PipelineManager __instance)
        {
            var avatarManager = __instance.gameObject.GetComponentInParent<VRCAvatarManager>();
            //var avatarManager = VRCPlayer.field_Internal_Static_VRCPlayer_0?.prop_VRCAvatarManager_0;
            if (avatarManager == null)
                return;

            //if (__instance.gameObject.Pointer != VRCPlayer.field_Internal_Static_VRCPlayer_0.prop_VRCAvatarManager_0.field_Private_GameObject_0.Pointer)
            //    return;
            AltNetIk.Instance.OnAvatarChange(avatarManager);
        }

        private static void OnPhotonInstanceChanged()
        {
            AltNetIk.Logger.Msg("Instance changed");
            AltNetIk.Instance.OnPhotonInstanceChanged();
        }

        private static void OnRoomLeave()
        {
            AltNetIk.Logger.Msg("OnRoomLeave");
            AltNetIk.Instance.ResetInstance();
        }

        private static bool Av3PoseUpdate(PoseRemoteUpdate __instance)
        {
            var photonView = __instance.gameObject.GetComponentInParent<PhotonView>();
            var photonId = photonView.field_Private_Int32_0;
            AltNetIk.Logger.Msg($"Av3PoseUpdate {photonId}");

            bool hasBoneData = AltNetIk.receiverPlayerData.TryGetValue(photonId, out AltNetIk.PlayerData boneData);
            if (!hasBoneData)
                return true;

            if (boneData.active)
            {
                AltNetIk.Logger.Msg($"Av3PoseUpdate disabled {photonId}");
                __instance.enabled = false;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns whether the given method is using another the other given method.
        /// </summary>
        /// <param name="method">The method to check</param>
        /// <param name="methodName">The name of the method that is used by the given method</param>
        /// <param name="type">The type of the method that is used by the given method</param>
        private static bool CheckUsing(MethodInfo method, string methodName, Type type = null)
        {
            foreach (var instance in XrefScanner.XrefScan(method))
            {
                if (instance.Type == XrefType.Method)
                {
                    try
                    {
                        if ((type == null || instance.TryResolve().DeclaringType == type) && instance.TryResolve().Name.Contains(methodName))
                            return true;
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
            return false;
        }
    }
}