using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UIExpansionKit.API;
using Delegate = Il2CppSystem.Delegate;
using VRC.Core;
using System.Linq;
using UnityEngine.UI;
using VRC;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using LiteNetLib;
using LiteNetLib.Utils;
using VRC.Playables;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AltNetIk
{
    public partial class AltNetIk : MelonMod
    {
        internal static AltNetIk Instance { get; private set; }
        private const string ModID = BuildInfo.Name;
        public static MelonLogger.Instance Logger;
        public static bool hasQmUiInit = false;
        public static bool IsConnected = false;
        public static bool IsSending = false;
        public static bool IsReceiving = true;
        public static bool IsFrozen = false;

        public static EventBasedNetListener listener;
        public static NetManager client;
        public static NetPeer serverPeer;
        private static readonly NetPacketProcessor netPacketProcessor = new NetPacketProcessor();

        private static ConcurrentDictionary<int, PlayerData> receiverPlayerData = new ConcurrentDictionary<int, PlayerData>();
        private static Dictionary<int, GameObject> frozenPlayers = new Dictionary<int, GameObject>();
        private static Dictionary<int, NamePlateInfo> PlayerNamePlates = new Dictionary<int, NamePlateInfo>();

        private static string currentInstanceIdHash;
        private static int currentPhotonId = 0;

        private static ConcurrentDictionary<int, ReceiverPacketData> receiverPacketData = new ConcurrentDictionary<int, ReceiverPacketData>();
        private static Dictionary<int, DataBank> receiverLastPacket = new Dictionary<int, DataBank>();
        private static PlayerData senderPlayerData = new PlayerData();

        private static PacketData.Quaternion[] netRotations;
        private static PacketData senderPacketData = new PacketData();
        private static ParamData senderParamData = new ParamData();

        private static List<string> boneNames = new List<string>();

        public static Dictionary<string, Transform> buttons = new Dictionary<string, Transform>();
        public static string color(string c, string s) { return $"<color={c}>{s}</color>"; } // stolen from MintLily

        private static bool autoConnect;
        private static bool qmButtonToggles;
        private static bool disableLerp;
        private static string serverIP;
        private static int serverPort;
        private static Int64 lastUpdate;
        private static Int64 ReconnectTimer;
        private static Int64 ReconnectLastAttempt;

        internal delegate void BoolPropertySetterDelegate(IntPtr @this, bool value);
        internal static BoolPropertySetterDelegate _boolPropertySetterDelegate;
        internal static void BoolPropertySetter(IntPtr @this, bool value)
        {
            _boolPropertySetterDelegate(@this, value);
        }
        internal delegate void IntPropertySetterDelegate(IntPtr @this, int value);
        internal static IntPropertySetterDelegate _intPropertySetterDelegate;
        internal static void IntPropertySetter(IntPtr @this, int value)
        {
            _intPropertySetterDelegate(@this, value);
        }
        internal delegate void FloatPropertySetterDelegate(IntPtr @this, float value);
        internal static FloatPropertySetterDelegate _floatPropertySetterDelegate;
        internal static void FloatPropertySetter(IntPtr @this, float value)
        {
            _floatPropertySetterDelegate(@this, value);
        }

        private bool _streamSafe;
        public override void OnApplicationStart()
        {
            _streamSafe = Environment.GetCommandLineArgs().Contains("-streamsafe");
            Logger = new MelonLogger.Instance(BuildInfo.Name, ConsoleColor.Magenta);
            Instance = this;

            MelonPreferences.CreateCategory(ModID, ModID);
            MelonPreferences.CreateEntry(ModID, "AutoConnect", true, "Auto connect to server on startup");
            MelonPreferences.CreateEntry(ModID, "QMButtonToggles", true, "QM toggle buttons for Sender/Receiver");
            MelonPreferences.CreateEntry(ModID, "DisableLerp", false, "Disable receiver interpolation");
            MelonPreferences.CreateEntry(ModID, "ServerIP", "ik.qwertyuiop.nz", "Server IP");
            MelonPreferences.CreateEntry(ModID, "ServerPort", 9052, "Server Port");
            autoConnect = MelonPreferences.GetEntryValue<bool>(ModID, "AutoConnect");
            qmButtonToggles = MelonPreferences.GetEntryValue<bool>(ModID, "QMButtonToggles");
            disableLerp = MelonPreferences.GetEntryValue<bool>(ModID, "DisableLerp");
            serverIP = MelonPreferences.GetEntryValue<string>(ModID, "ServerIP");
            serverPort = MelonPreferences.GetEntryValue<int>(ModID, "ServerPort");

            MelonCoroutines.Start(UiInit());
            Camera.onPreRender = Delegate.Combine(Camera.onPreRender, (Camera.CameraCallback)OnVeryLateUpdate).Cast<Camera.CameraCallback>();

            boneNames = HumanTrait.BoneName.ToList();

            ExpansionKitApi.GetExpandedMenu(ExpandedMenu.QuickMenu).AddSimpleButton("AltNetIk " + color("#ff0000", "Disconnected"), ConnectToggle, (button) => buttons["ConnectToggle"] = button.transform);
            ExpansionKitApi.GetExpandedMenu(ExpandedMenu.QuickMenu).AddSimpleButton("SendIK " + color("#00ff00", "Enabled"), ToggleSend, (button) => buttons["ToggleSend"] = button.transform);
            ExpansionKitApi.GetExpandedMenu(ExpandedMenu.QuickMenu).AddSimpleButton("ReceiveIK " + color("#00ff00", "Enabled"), ToggleReceive, (button) => buttons["ToggleReceive"] = button.transform);

            netPacketProcessor.RegisterNestedType<PacketData.Quaternion>();
            netPacketProcessor.RegisterNestedType<PacketData.Vector3>();
            netPacketProcessor.Subscribe(OnEventPacketReceived, () => new EventData());
            netPacketProcessor.Subscribe(OnPacketReceived, () => new PacketData());
            netPacketProcessor.Subscribe(OnParamPacketReceived, () => new ParamData());

            unsafe
            {
                // stolen from who knows where
                var param_prop_bool_set = (IntPtr)typeof(AvatarParameter).GetField("NativeMethodInfoPtr_set_boolVal_Public_Virtual_Final_New_set_Void_Boolean_0", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                MelonUtils.NativeHookAttach(param_prop_bool_set, new Action<IntPtr, bool>(BoolPropertySetter).Method.MethodHandle.GetFunctionPointer());
                _boolPropertySetterDelegate = Marshal.GetDelegateForFunctionPointer<BoolPropertySetterDelegate>(*(IntPtr*)(void*)param_prop_bool_set);

                var param_prop_int_set = (IntPtr)typeof(AvatarParameter).GetField("NativeMethodInfoPtr_set_intVal_Public_Virtual_Final_New_set_Void_Int32_0", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                MelonUtils.NativeHookAttach(param_prop_int_set, new Action<IntPtr, int>(IntPropertySetter).Method.MethodHandle.GetFunctionPointer());
                _intPropertySetterDelegate = Marshal.GetDelegateForFunctionPointer<IntPropertySetterDelegate>(*(IntPtr*)(void*)param_prop_int_set);

                var param_prop_float_set = (IntPtr)typeof(AvatarParameter).GetField("NativeMethodInfoPtr_set_floatVal_Public_Virtual_Final_New_set_Void_Single_0", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                MelonUtils.NativeHookAttach(param_prop_float_set, new Action<IntPtr, float>(FloatPropertySetter).Method.MethodHandle.GetFunctionPointer());
                _floatPropertySetterDelegate = Marshal.GetDelegateForFunctionPointer<FloatPropertySetterDelegate>(*(IntPtr*)(void*)param_prop_float_set);
            }

            if (autoConnect)
                MelonCoroutines.Start(Connect());
        }

        public static IEnumerator UiInit()
        {
            while (VRCUiManager.prop_VRCUiManager_0 == null)
                yield return null;
            while (GameObject.Find("UserInterface").transform.Find("Canvas_QuickMenu(Clone)") == null)
                yield return null;

            hasQmUiInit = true;
            Patches.DoPatches();
        }

        public override void OnUpdate()
        {
            if (client != null)
                client.PollEvents();

            var date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (date - lastUpdate >= 500)
            {
                lastUpdate = date;
                AutoReconnect();
                TimeoutCheck();
                UpdateNamePlates();
            }
        }

        public override void OnPreferencesSaved()
        {
            autoConnect = MelonPreferences.GetEntryValue<bool>(ModID, "AutoConnect");
            qmButtonToggles = MelonPreferences.GetEntryValue<bool>(ModID, "QMButtonToggles");
            disableLerp = MelonPreferences.GetEntryValue<bool>(ModID, "DisableLerp");
            var newServerIP = MelonPreferences.GetEntryValue<string>(ModID, "ServerIP");
            var newServerPort = MelonPreferences.GetEntryValue<int>(ModID, "ServerPort");
            if (newServerIP != serverIP || newServerPort != serverPort)
            {
                serverIP = newServerIP;
                serverPort = newServerPort;
                if (IsConnected)
                {
                    Disconnect();
                    MelonCoroutines.Start(Connect());
                }
            }
        }

        public override void OnApplicationQuit()
        {
            if (client != null)
            {
                client.DisconnectAll();
                client.Stop();
            }
        }

        private void ToggleSend()
        {
            IsSending = !IsSending;
            if (!IsSending)
                SendDisconnect();
            UpdateAllButtons();
        }

        private void ToggleReceive()
        {
            IsReceiving = !IsReceiving;
            if (!IsReceiving)
            {
                DisableReceivers();
            }
            UpdateAllButtons();
        }

        private void ToggleLerp()
        {
            disableLerp = !disableLerp;
            UpdateAllButtons();
        }

        public void OnInstanceChanged(ApiWorld world, ApiWorldInstance instance)
        {
            ResetInstance();

            IsSending = false;
            string instanceId = $"{world.id}:{instance.id}";
            HashAlgorithm algorithm = new MD5CryptoServiceProvider();
            byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(instanceId));
            currentInstanceIdHash = Convert.ToBase64String(hash);
        }

        public void ResetInstance()
        {
            currentInstanceIdHash = String.Empty;
            currentPhotonId = 0;
            receiverPacketData.Clear();
            receiverLastPacket.Clear();
            frozenPlayers.Clear();
            receiverPlayerData.Clear();
            PlayerNamePlates.Clear();
        }

        public void OnPlayerJoined(Player player)
        {
            if (player == null)
                return;
            VRCPlayer currentUser = VRCPlayer.field_Internal_Static_VRCPlayer_0;
            int photonId = player.field_Private_Player_0.field_Private_Int32_0;
            if (player.gameObject == currentUser.gameObject)
            {
                currentPhotonId = photonId;
                MelonCoroutines.Start(SendLocationData());
            }
            else
            {
                SetNamePlate(photonId, player);
            }
        }

        public void OnPlayerLeft(Player player)
        {
            if (player == null)
                return;
            int photonId = player.field_Private_Player_0.field_Private_Int32_0;
            if (photonId != currentPhotonId)
            {
                PlayerNamePlates.Remove(photonId);
                receiverPlayerData.TryRemove(photonId, out _);
                receiverPacketData.TryRemove(photonId, out _);
                receiverLastPacket.Remove(photonId);
                RemoveFreeze(photonId);
            }
        }

        public void OnAvatarChange(VRCAvatarManager avatarManager, ApiAvatar __1, GameObject gameObject)
        {
            VRCPlayer player = avatarManager.field_Private_VRCPlayer_0;
            if (player == null)
                return;

            short avatarKind = (short)avatarManager.field_Private_AvatarKind_0;

            if (player == VRCPlayer.field_Internal_Static_VRCPlayer_0)
                SetSenderBones(player, avatarManager, avatarKind);
            else
                SetReceiverBones(player, avatarManager, avatarKind);
        }

        public void OnAvatarInit(VRCAvatarManager avatarManager, GameObject __1)
        {
            VRCPlayer player = avatarManager.field_Private_VRCPlayer_0;
            if (player == null) return;

            if (player == VRCPlayer.field_Internal_Static_VRCPlayer_0)
            {
                senderPlayerData.avatarKind = (short)avatarManager.field_Private_AvatarKind_0;
            }
            else
            {
                int photonId = player.prop_PhotonView_0.field_Private_Int32_0;
                bool hasBoneData = receiverPlayerData.TryGetValue(photonId, out PlayerData boneData);
                if (!hasBoneData)
                    return;

                boneData.avatarKind = (short)avatarManager.field_Private_AvatarKind_0;
                receiverPlayerData.AddOrUpdate(photonId, boneData, (k, v) => boneData);
            }
        }

        public static void UpdateButtonText(string buttonName, string text)
        {
            try
            {
                if (buttons.ContainsKey(buttonName) && buttons[buttonName] != null)
                    buttons[buttonName].GetComponentInChildren<Text>().text = text;
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
            }
        }

        public static void UpdateAllButtons()
        {
            if (!hasQmUiInit)
                return;

            if (IsConnected)
                UpdateButtonText("ConnectToggle", "AltNetIk " + color("#00ff00", "Connected"));
            else
                UpdateButtonText("ConnectToggle", "AltNetIk " + color("#ff0000", "Disconnected"));

            if (buttons.ContainsKey("ToggleSend") && buttons.ContainsKey("ToggleReceive"))
            {
                if (qmButtonToggles)
                {
                    buttons["ToggleSend"].gameObject.SetActive(true);
                    buttons["ToggleReceive"].gameObject.SetActive(true);
                }
                else
                {
                    buttons["ToggleSend"].gameObject.SetActive(false);
                    buttons["ToggleReceive"].gameObject.SetActive(false);
                }
            }

            if (IsSending)
                UpdateButtonText("ToggleSend", "SendIK " + color("#00ff00", "Enabled"));
            else
                UpdateButtonText("ToggleSend", "SendIK " + color("#ff0000", "Disabled"));

            if (IsReceiving)
                UpdateButtonText("ToggleReceive", "ReceiveIK " + color("#00ff00", "Enabled"));
            else
                UpdateButtonText("ToggleReceive", "ReceiveIK " + color("#ff0000", "Disabled"));
        }

        public static Quaternion LerpUnclamped(PacketData.Quaternion q1, PacketData.Quaternion q2, float t)
        {
            float negate = (q1.x * q2.x + q1.y * q2.y + q1.z * q2.z + q1.w * q2.w < 0) ? -1f : 1f;
            Quaternion r = new Quaternion
            {
                x = q1.x + t * (negate * q2.x - q1.x),
                y = q1.y + t * (negate * q2.y - q1.y),
                z = q1.z + t * (negate * q2.z - q1.z),
                w = q1.w + t * (negate * q2.w - q1.w)
            };
            float len = (float)Math.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w);
            if (len > 0)
            {
                if (r.w < 0)
                    len = -len;
                r.x = r.x / len;
                r.y = r.y / len;
                r.z = r.z / len;
                r.w = r.w / len;
            }
            return r;
        }

        public static Vector3 LerpUnclamped(PacketData.Vector3 v1, PacketData.Vector3 v2, float t)
        {
            Vector3 r = new Vector3(
                v1.x + (v2.x - v1.x) * t,
                v1.y + (v2.y - v1.y) * t,
                v1.z + (v2.z - v1.z) * t
            );
            return r;
        }

        public static int GetDeltaTime(ref int deltaTime, int deltaInt)
        {
            if (deltaTime == 0)
            {
                deltaTime = deltaInt;
                return deltaTime;
            }
            int deltaTimeSmoothing = 5;
            deltaTime = (deltaInt + deltaTime * deltaTimeSmoothing) / (deltaTimeSmoothing + 1);
            return deltaTime;
        }
    }
}
