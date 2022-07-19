using LiteNetLib;
using LiteNetLib.Utils;
using MelonLoader;
using Newtonsoft.Json;
using ReMod.Core.Notification;
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
using System.Text.RegularExpressions;
using ReMod.Core;
using ReMod.Core.Managers;
using ReMod.Core.UI.QuickMenu;
using UnityEngine;
using VRC;
using VRC.Core;
using VRC.Playables;
using Delegate = Il2CppSystem.Delegate;
using ReMod.Core.Notification;

namespace AltNetIk
{
    public partial class AltNetIk : ModComponent
    {
        internal static AltNetIk Instance { get; private set; }
        private const string ModID = BuildInfo.Name;
        public MelonLogger.Instance Logger { get; }
        private bool IsConnected = false;
        private bool IsSending = false;
        private bool IsSendingBlocked = false;
        private bool IsReceiving = true;
        public static bool IsFrozen = false; // needs to stay public static (for now)

        private EventBasedNetListener listener;
        private NetManager client;
        private NetPeer serverPeer;
        private readonly NetPacketProcessor netPacketProcessor = new NetPacketProcessor();

        private ConcurrentDictionary<int, PlayerData> receiverPlayerData = new ConcurrentDictionary<int, PlayerData>();
        private Dictionary<int, GameObject> frozenPlayers = new Dictionary<int, GameObject>();
        private Dictionary<int, NamePlateInfo> playerNamePlates = new Dictionary<int, NamePlateInfo>();

        private string currentInstanceIdHash;
        private string currentInstanceRegion;
        private int currentPhotonId;

        private ConcurrentDictionary<int, ReceiverPacketData> receiverPacketData = new ConcurrentDictionary<int, ReceiverPacketData>();
        private ConcurrentDictionary<int, DataBank> receiverLastPacket = new ConcurrentDictionary<int, DataBank>();
        private static ConcurrentDictionary<int, ParamData> receiverParamData = new ConcurrentDictionary<int, ParamData>();
        private PlayerData senderPlayerData = new PlayerData();

        private System.Numerics.Quaternion[] netRotations;
        private PacketData senderPacketData = new PacketData();
        private ParamData senderParamData = new ParamData();

        public static string StringToColor(string c, string s)
        {
            return $"<color={c}>{s}</color>";
        } // stolen from MintLily

        private bool autoConnect;
        private bool _streamSafe;
        private bool enableLerp;
        private bool namePlates;
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

        private readonly Dictionary<string, ReMenuButton> buttons = new Dictionary<string, ReMenuButton>();
        private readonly Dictionary<string, ReMenuToggle> toggles = new Dictionary<string, ReMenuToggle>();

        public AltNetIk()
        {
            Logger = new MelonLogger.Instance(BuildInfo.Name, ConsoleColor.Magenta);
            Instance = this;

            var ourAssembly = Assembly.GetExecutingAssembly();
            var resources = ourAssembly.GetManifestResourceNames();
            foreach (var resource in resources)
            {
                if (!resource.EndsWith(".png"))
                    continue;

                var stream = ourAssembly.GetManifestResourceStream(resource);

                using var ms = new MemoryStream();
                stream!.CopyTo(ms);
                var resourceName = Regex.Match(resource, @"([a-zA-Z\d\-_]+)\.png").Groups[1].ToString();
                ResourceManager.LoadSprite("altnetik", resourceName, ms.ToArray());
            }

            MelonPreferences.CreateCategory(ModID, ModID);
            MelonPreferences.CreateEntry(ModID, "AutoConnect", true, "Auto connect to server on startup");
            MelonPreferences.CreateEntry(ModID, "NamePlates", true, "Nameplate stats");
            MelonPreferences.CreateEntry(ModID, "EnableLerp", true, "Receiver interpolation");
            MelonPreferences.CreateEntry(ModID, "FloatPrecision", true, "Avatar parameter float precision");
            autoConnect = MelonPreferences.GetEntryValue<bool>(ModID, "AutoConnect");
            namePlates = MelonPreferences.GetEntryValue<bool>(ModID, "NamePlates");
            enableLerp = MelonPreferences.GetEntryValue<bool>(ModID, "EnableLerp");
            floatPrecision = MelonPreferences.GetEntryValue<bool>(ModID, "FloatPrecision");

            if (!MelonHandler.Mods.Any(m => m.Info.Name == "TabExtension"))
                Logger.Warning("TabExtension is missing, to fix broken quick menu tabs install it from here: https://github.com/DragonPlayerX/TabExtension/releases/latest");

            _streamSafe = Environment.GetCommandLineArgs().Contains("-streamsafe");

            netPacketProcessor.RegisterNestedType<System.Numerics.Quaternion>(Serializers.SerializeQuaternion, Serializers.DeserializeQuaternion);
            netPacketProcessor.RegisterNestedType<System.Numerics.Vector3>(Serializers.SerializeVector3, Serializers.DeserializeVector3);
            netPacketProcessor.Subscribe(OnEventPacketReceived, () => new EventData());
            netPacketProcessor.Subscribe(OnPacketReceived, () => new PacketData());
            netPacketProcessor.Subscribe(OnParamPacketReceived, () => new ParamData());

            Patches.DoPatches();

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

        public override void OnUiManagerInit(UiManager uiManager)
        {
            var menu = uiManager.MainMenu.AddMenuPage("AltNetIK", "Settings for the AltNetIK module", ResourceManager.GetSprite("remod.cogwheel"));
            buttons["ConnectToggle"] = menu.AddButton("Server\n" + StringToColor("#ff0000", "Disconnected"), "Connect/Disconnect from AltNetIk server.",
                Instance.ConnectToggle, ResourceManager.GetSprite("altnetik.Logo"));
            buttons["ToggleSend"] = menu.AddButton("Send\n" + StringToColor("#00ff00", "Enabled"), "Toggle sending data to server (automatically managed).",
                Instance.ToggleSend, ResourceManager.GetSprite("altnetik.Up"));
            buttons["ToggleReceive"] = menu.AddButton("Receive\n" + StringToColor("#00ff00", "Enabled"), "Toggle receiving data from server.",
                Instance.ToggleReceive, ResourceManager.GetSprite("altnetik.Down"));
            buttons["Ping"] = menu.AddButton($"Ping: {serverPeer?.RoundTripTime}", "Current ping to AltNetIk server.", () =>
            {
                Logger.Msg($"Ping{(char)7}");
                NotificationSystem.EnqueueNotification("AltNetIk", "Ping", 1f);
            }, ResourceManager.GetSprite("altnetik.ping"));

            toggles["EnableLerp"] = menu.AddToggle("Receiver Interpolation", "Toggle receiver interpolation.", state =>
            {
                enableLerp = state;
                MelonPreferences.SetEntryValue(ModID, "EnableLerp", state);
            }, enableLerp);
            toggles["NameplateStats"] = menu.AddToggle("Nameplate Stats", "Toggle nameplate stats.", state =>
            {
                namePlates = state;
                MelonPreferences.SetEntryValue(ModID, "NamePlates", state);
            }, namePlates);
            toggles["AutoConnect"] = menu.AddToggle("Auto Connect", "Automatically connect to AltNetIk server.", (state) =>
            {
                autoConnect = state;
                MelonPreferences.SetEntryValue(ModID, "AutoConnect", state);
            }, autoConnect);
        }

        public override void OnUpdate()
        {
            client?.PollEvents();
            ApplyAvatarParams();

            var date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (date - lastUpdate < 500)
                return;

            lastUpdate = date;
            AutoReconnect();

            if (IsConnected)
            {
                TimeoutCheck();
                UpdateNamePlates();
                UpdatePing();
            }
        }

        public override void OnPreferencesSaved()
        {
            autoConnect = MelonPreferences.GetEntryValue<bool>(ModID, "AutoConnect");
            namePlates = MelonPreferences.GetEntryValue<bool>(ModID, "NamePlates");
            enableLerp = MelonPreferences.GetEntryValue<bool>(ModID, "EnableLerp");
            UpdateToggleState("NameplateStats", namePlates);
            UpdateToggleState("EnableLerp", enableLerp);
            var newFloatPrecision = MelonPreferences.GetEntryValue<bool>(ModID, "FloatPrecision");
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

        private void ToggleSend()
        {
            IsSending = !IsSending;
            if (!IsSending)
                StopSending();
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
            enableLerp = !enableLerp;
            UpdateAllButtons();
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

            NegotiateServer();
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
                    NotificationSystem.EnqueueNotification("AltNetIk", connectResponse.message);
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
                    NotificationSystem.EnqueueNotification("AltNetIk", connectResponse.message);
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

        public override void OnLeftRoom()
        {
            ResetInstance();
        }

        private void ResetInstance()
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

        public override void OnPlayerJoined(Player player)
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

        public override void OnPlayerLeft(Player player)
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

        public override void OnAvatarIsReady(VRCPlayer vrcPlayer)
        {
            // VRCAvatarManager avatarManager, ApiAvatar __1, GameObject gameObject
            var avatarManager = vrcPlayer.field_Private_VRCAvatarManager_0;

            short avatarKind = (short)avatarManager.field_Private_AvatarKind_0;

            if (vrcPlayer == VRCPlayer.field_Internal_Static_VRCPlayer_0)
                SetSenderBones(vrcPlayer, avatarManager, avatarKind);
            else
                SetReceiverBones(vrcPlayer, avatarManager, avatarKind);
        }

        public void OnAvatarInit(VRCAvatarManager avatarManager, GameObject __1)
        {
            var player = avatarManager.field_Private_VRCPlayer_0;
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

        private static int GetDeltaTime(ref int deltaTime, int deltaInt)
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

        private void UpdatePing()
        {
            int ping = 0;
            if (serverPeer != null)
                ping = serverPeer.RoundTripTime;
            UpdateButtonText("Ping", $"Ping: {ping}");
        }

        private void UpdateButtonText(string buttonName, string text)
        {
            try
            {
                if (buttons.ContainsKey(buttonName) && buttons[buttonName] != null)
                    buttons[buttonName].Text = text;
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
            }
        }

        private void UpdateToggleState(string toggleName, bool state)
        {
            try
            {
                if (toggles.ContainsKey(toggleName) && toggles[toggleName] != null)
                    toggles[toggleName].Toggle(state);
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
            }
        }

        public void UpdateAllButtons()
        {
            if (IsConnected)
                UpdateButtonText("ConnectToggle", "Server\n" + StringToColor("#00ff00", "Connected"));
            else
                UpdateButtonText("ConnectToggle", "Server\n" + StringToColor("#ff0000", "Disconnected"));

            if (IsSending)
                UpdateButtonText("ToggleSend", "Send\n" + StringToColor("#00ff00", "Enabled"));
            else
                UpdateButtonText("ToggleSend", "Send\n" + StringToColor("#ff0000", "Disabled"));

            if (IsReceiving)
                UpdateButtonText("ToggleReceive", "Receive\n" + StringToColor("#00ff00", "Enabled"));
            else
                UpdateButtonText("ToggleReceive", "Receive\n" + StringToColor("#ff0000", "Disabled"));

            if (buttons.ContainsKey("ToggleSend"))
                buttons["ToggleSend"].Interactable = !IsSendingBlocked;
            if (IsSendingBlocked)
                UpdateButtonText("ToggleSend", "Send\n" + StringToColor("#ffff00", "SDK2/Chair Disabled"));

            UpdatePing();
        }
    }
}