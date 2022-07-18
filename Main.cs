using LiteNetLib;
using LiteNetLib.Utils;
using MelonLoader;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using VRC;
using VRC.Core;
using VRC.Playables;
using Delegate = Il2CppSystem.Delegate;

namespace AltNetIk
{
    public partial class AltNetIk : MelonMod
    {
        internal static AltNetIk Instance { get; private set; }
        public const string ModID = BuildInfo.Name;
        public static MelonLogger.Instance Logger;
        public static bool IsConnected = false;
        public static bool IsSending = false;
        public static bool IsSendingBlocked = false;
        public static bool IsReceiving = true;
        public static bool IsFrozen = false;

        private EventBasedNetListener listener;
        private NetManager client;
        public static NetPeer serverPeer;
        private readonly NetPacketProcessor netPacketProcessor = new NetPacketProcessor();

        private static ConcurrentDictionary<int, PlayerData> receiverPlayerData = new ConcurrentDictionary<int, PlayerData>();
        private static Dictionary<int, GameObject> frozenPlayers = new Dictionary<int, GameObject>();
        private static Dictionary<int, NamePlateInfo> playerNamePlates = new Dictionary<int, NamePlateInfo>();

        private string currentInstanceIdHash;
        private string currentInstanceRegion;
        private int currentPhotonId;

        private static ConcurrentDictionary<int, ReceiverPacketData> receiverPacketData = new ConcurrentDictionary<int, ReceiverPacketData>();
        private ConcurrentDictionary<int, DataBank> receiverLastPacket = new ConcurrentDictionary<int, DataBank>();
        private static ConcurrentDictionary<int, ParamData> receiverParamData = new ConcurrentDictionary<int, ParamData>();
        private PlayerData senderPlayerData = new PlayerData();

        private System.Numerics.Quaternion[] netRotations;
        private PacketData senderPacketData = new PacketData();
        private ParamData senderParamData = new ParamData();

        public static string color(string c, string s)
        { return $"<color={c}>{s}</color>"; } // stolen from MintLily

        public static bool autoConnect;
        private bool _streamSafe;
        public static bool enableLerp;
        public static bool namePlates;
        public static bool customServer;
        private string serverIP;
        private int serverPort;
        private string currentServerIP;
        private int currentServerPort;
        private bool floatPrecision;
        private Int64 lastUpdate;
        private Int64 ReconnectTimer;
        private Int64 ReconnectLastAttempt;
        private static List<IntPtr> boolParamsInUse = new List<IntPtr>();
        private static List<IntPtr> intParamsInUse = new List<IntPtr>();
        private static List<IntPtr> floatParamsInUse = new List<IntPtr>();
        private static bool skipSettingParam;

        internal delegate void BoolPropertySetterDelegate(IntPtr @this, bool value);

        internal static BoolPropertySetterDelegate _boolPropertySetterDelegate;

        internal static void BoolPropertySetter(IntPtr @this, bool value)
        {
            if (skipSettingParam && boolParamsInUse.Contains(@this))
                return;

            _boolPropertySetterDelegate(@this, value);
        }

        internal delegate void IntPropertySetterDelegate(IntPtr @this, int value);

        internal static IntPropertySetterDelegate _intPropertySetterDelegate;

        internal static void IntPropertySetter(IntPtr @this, int value)
        {
            if (skipSettingParam && intParamsInUse.Contains(@this))
                return;

            _intPropertySetterDelegate(@this, value);
        }

        internal delegate void FloatPropertySetterDelegate(IntPtr @this, float value);

        internal static FloatPropertySetterDelegate _floatPropertySetterDelegate;

        internal static void FloatPropertySetter(IntPtr @this, float value)
        {
            if (skipSettingParam && floatParamsInUse.Contains(@this))
                return;

            _floatPropertySetterDelegate(@this, value);
        }

        public override void OnApplicationStart()
        {
            Logger = new MelonLogger.Instance(BuildInfo.Name, ConsoleColor.Magenta);
            Instance = this;

            MelonPreferences.CreateCategory(ModID, ModID);
            MelonPreferences.CreateEntry(ModID, "ServerAutoConnect", true, "Auto connect on startup");
            MelonPreferences.CreateEntry(ModID, "NamePlates", true, "Nameplate stats");
            MelonPreferences.CreateEntry(ModID, "EnableLerp", true, "Receiver interpolation");
            MelonPreferences.CreateEntry(ModID, "CustomServer", false, "Enable custom server");
            MelonPreferences.CreateEntry(ModID, "ServerIP", "", "Custom server IP");
            MelonPreferences.CreateEntry(ModID, "ServerPort", 9052, "Custom server Port");
            MelonPreferences.CreateEntry(ModID, "FloatPrecision", true, "Avatar parameter float precision");
            autoConnect = MelonPreferences.GetEntryValue<bool>(ModID, "ServerAutoConnect");
            namePlates = MelonPreferences.GetEntryValue<bool>(ModID, "NamePlates");
            enableLerp = MelonPreferences.GetEntryValue<bool>(ModID, "EnableLerp");
            customServer = MelonPreferences.GetEntryValue<bool>(ModID, "CustomServer");
            serverIP = MelonPreferences.GetEntryValue<string>(ModID, "ServerIP");
            serverPort = MelonPreferences.GetEntryValue<int>(ModID, "ServerPort");
            floatPrecision = MelonPreferences.GetEntryValue<bool>(ModID, "FloatPrecision");

            if (!MelonHandler.Mods.Any(m => m.Info.Name == "TabExtension"))
                Logger.Warning("TabExtension is missing, to fix broken quick menu tabs install it from here: https://github.com/DragonPlayerX/TabExtension/releases/latest");

            _streamSafe = Environment.GetCommandLineArgs().Contains("-streamsafe");

            var ReModCoreUpdaterPath = Path.Combine(MelonUtils.GetGameDataDirectory(), "../Plugins/ReMod.Core.Updater.dll");
            if (!File.Exists(ReModCoreUpdaterPath))
            {
                Logger.Error("ReMod.Core.Updater.dll is missing, it's required to use this mod.");
                using (var client = new WebClient())
                {
                    client.DownloadFile("https://api.vrcmg.com/v1/mods/download/328", ReModCoreUpdaterPath);
                }
                Logger.Warning("ReMod.Core.Updater.dll has been downloaded into the plugins folder, please restart your game to load this mod.");
                return;
            }

            netPacketProcessor.RegisterNestedType<System.Numerics.Quaternion>(Serializers.SerializeQuaternion, Serializers.DeserializeQuaternion);
            netPacketProcessor.RegisterNestedType<System.Numerics.Vector3>(Serializers.SerializeVector3, Serializers.DeserializeVector3);
            netPacketProcessor.Subscribe(OnEventPacketReceived, () => new EventData());
            netPacketProcessor.Subscribe(OnPacketReceived, () => new PacketData());
            netPacketProcessor.Subscribe(OnParamPacketReceived, () => new ParamData());

            MelonCoroutines.Start(UiInit());
            Camera.onPreRender = Delegate.Combine(Camera.onPreRender, (Camera.CameraCallback)OnVeryLateUpdate).Cast<Camera.CameraCallback>();
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
        }

        public IEnumerator UiInit()
        {
            while (VRCUiManager.prop_VRCUiManager_0 == null)
                yield return new WaitForSeconds(0.1f);
            while (GameObject.Find("UserInterface").transform.Find("Canvas_QuickMenu(Clone)") == null)
                yield return new WaitForSeconds(0.1f);

            Patches.DoPatches();
            Buttons.SetupButtons();
        }

        public override void OnUpdate()
        {
            client?.PollEvents();
            ApplyAvatarParams();

            var date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (date - lastUpdate >= 500)
            {
                lastUpdate = date;
                AutoReconnect();
                if (IsConnected)
                {
                    TimeoutCheck();
                    UpdateNamePlates();
                    Buttons.UpdatePing();
                }
            }
        }

        public override void OnPreferencesSaved()
        {
            autoConnect = MelonPreferences.GetEntryValue<bool>(ModID, "ServerAutoConnect");
            namePlates = MelonPreferences.GetEntryValue<bool>(ModID, "NamePlates");
            enableLerp = MelonPreferences.GetEntryValue<bool>(ModID, "EnableLerp");

            var newServerIP = MelonPreferences.GetEntryValue<string>(ModID, "ServerIP");
            var newServerPort = MelonPreferences.GetEntryValue<int>(ModID, "ServerPort");
            var newFloatPrecision = MelonPreferences.GetEntryValue<bool>(ModID, "FloatPrecision");
            var newCustomServer = MelonPreferences.GetEntryValue<bool>(ModID, "CustomServer");

            Buttons.UpdateToggleState("NameplateStats", namePlates);
            Buttons.UpdateToggleState("EnableLerp", enableLerp);
            Buttons.UpdateToggleState("AutoConnect", autoConnect);
            Buttons.UpdateToggleState("CustomServer", newCustomServer);

            if (newCustomServer != customServer || newServerIP != serverIP || newServerPort != serverPort)
            {
                serverIP = newServerIP;
                serverPort = newServerPort;
                customServer = newCustomServer;
                if (IsConnected)
                {
                    // reconnect to apply new settings
                    Disconnect();
                    MelonCoroutines.Start(Connect());
                }
            }

            if (newFloatPrecision != floatPrecision)
            {
                int paramCount = senderPlayerData.parameters.Count;
                int paramBytesLength = (paramCount * 2) + 2;
                if (newFloatPrecision)
                    paramBytesLength += senderPlayerData.floatParamCount;

                senderParamData.paramData = new byte[paramBytesLength];
                floatPrecision = newFloatPrecision;
            }
        }

        public override void OnApplicationQuit()
        {
            DisconnectSilent();
        }

        public void ToggleSend()
        {
            IsSending = !IsSending;
            if (!IsSending)
                StopSending();
            Buttons.UpdateAllButtons();
        }

        public void ToggleReceive()
        {
            IsReceiving = !IsReceiving;
            if (!IsReceiving)
            {
                DisableReceivers();
            }
            Buttons.UpdateAllButtons();
        }

        private void ToggleLerp()
        {
            enableLerp = !enableLerp;
            Buttons.UpdateAllButtons();
        }

        public void OnPhotonInstanceChanged()
        {
            //Regex regex = new Regex(@".*~region\((.*?)\)");
            //Match match = regex.Match(instance.id);
            //var instanceRegion = "us";
            //if (match.Success)
            //    instanceRegion = match.Groups[1].Value;
            ResetInstance();
            var photonServerIP = VRCNetworkingClient.field_Internal_Static_VRCNetworkingClient_0.field_Private_String_3;
            var worldId = RoomManager.field_Internal_Static_ApiWorldInstance_0.id;
            var region = RoomManager.field_Internal_Static_ApiWorldInstance_0.region;
            var instanceId = $"{worldId}:{photonServerIP}";
            HashAlgorithm algorithm = new MD5CryptoServiceProvider();
            byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(instanceId));
            currentInstanceRegion = region.ToString();
            currentInstanceIdHash = $"{currentInstanceRegion}-{Convert.ToBase64String(hash)}";

            if (!autoConnect)
                return;

            if (customServer)
            {
                if (!IsConnected)
                    MelonCoroutines.Start(Connect());
            }
            else
            {
                NegotiateServer();
            }
        }

        public void NegotiateServer()
        {
            var photonServerIP = VRCNetworkingClient.field_Internal_Static_VRCNetworkingClient_0.field_Private_String_3;
            var newServerIP = currentServerIP;
            var newServerPort = currentServerPort;
            var connectRequest = new ConnectRequest
            {
                photonId = currentPhotonId,
                photonServer = photonServerIP,
                instanceRegion = currentInstanceRegion,
                instanceHash = currentInstanceIdHash
            };
            var json = JsonConvert.SerializeObject(connectRequest);
            try
            {
                using var client = new WebClient();
                client.Headers.Add("user-agent", $"{BuildInfo.Name} {BuildInfo.Version}");
                client.Headers.Add("Content-Type", "application/json");
                string response = client.UploadString("https://altnetik.dev/connect", WebRequestMethods.Http.Put, json);
                var connectResponse = JsonConvert.DeserializeObject<ConnectResponse>(response);
                if (connectResponse.action == "Error")
                {
                    Logger.Error(connectResponse.message);
                    if (IsConnected)
                    {
                        ReconnectLastAttempt = 0;
                        DisconnectSilent();
                    }
                    return;
                }
                else if (!String.IsNullOrEmpty(connectResponse.message))
                {
                    Logger.Msg(connectResponse.message);
                }
                newServerIP = connectResponse.ip;
                newServerPort = connectResponse.port;
            }
            catch (WebException ex)
            {
                HttpWebResponse response = (HttpWebResponse)ex.Response;
                Logger.Error($"Error: {response.StatusCode}, failed to contact AltNetIk API server.");
                if (IsConnected)
                {
                    ReconnectLastAttempt = 0;
                    DisconnectSilent();
                }
                return;
            }
            catch
            {
                Logger.Error("Failed to contact AltNetIk API server.");
                if (IsConnected)
                {
                    ReconnectLastAttempt = 0;
                    DisconnectSilent();
                }
                return;
            }

            if (newServerIP != currentServerIP || newServerPort != currentServerPort)
            {
                currentServerIP = newServerIP;
                currentServerPort = newServerPort;
                // reconnect to apply new settings
                if (IsConnected)
                {
                    ReconnectLastAttempt = 0;
                    DisconnectSilent();
                }
            }
            if (!IsConnected)
                MelonCoroutines.Start(Connect());
        }

        public void ResetInstance()
        {
            IsSending = false;
            currentInstanceIdHash = String.Empty;
            currentInstanceRegion = String.Empty;
            currentPhotonId = 0;
            receiverPacketData.Clear();
            receiverLastPacket.Clear();
            frozenPlayers.Clear();
            receiverPlayerData.Clear();
            playerNamePlates.Clear();
            boolParamsInUse.Clear();
            intParamsInUse.Clear();
            floatParamsInUse.Clear();
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
                DisableReceiver(photonId);
                playerNamePlates.Remove(photonId);
                receiverPlayerData.TryRemove(photonId, out _);
            }
        }

        public void OnAvatarChange(VRCAvatarManager avatarManager)
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

        public void OnAvatarInit(VRCAvatarManager avatarManager)
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

        public static int GetDeltaTime(ref int deltaTime, int deltaInt)
        {
            if (deltaTime == 0)
            {
                deltaTime = deltaInt;
                return deltaTime;
            }
            int deltaTimeSmoothing = 10;
            deltaTime = (deltaInt + deltaTime * deltaTimeSmoothing) / (deltaTimeSmoothing + 1);
            return deltaTime;
        }
    }
}