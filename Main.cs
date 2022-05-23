using LiteNetLib;
using LiteNetLib.Utils;
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

namespace AltNetIk
{
    public partial class AltNetIk : ModComponent
    {
        internal static AltNetIk Instance { get; private set; }
        private const string ModID = BuildInfo.Name;
        public MelonLogger.Instance Logger { get; }
        private bool IsConnected = false;
        private bool IsSending = false;
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
        private int currentPhotonId = 0;

        private ConcurrentDictionary<int, ReceiverPacketData> receiverPacketData = new ConcurrentDictionary<int, ReceiverPacketData>();
        private ConcurrentDictionary<int, DataBank> receiverLastPacket = new ConcurrentDictionary<int, DataBank>();
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
        private string serverIP;
        private int serverPort;
        private Int64 lastUpdate;
        private Int64 ReconnectTimer;
        private Int64 ReconnectLastAttempt;

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
            autoConnect = MelonPreferences.GetEntryValue<bool>(ModID, "AutoConnect");
            namePlates = MelonPreferences.GetEntryValue<bool>(ModID, "NamePlates");
            enableLerp = MelonPreferences.GetEntryValue<bool>(ModID, "EnableLerp");
            serverIP = "188.40.191.108";
            serverPort = 9053;

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

            toggles["AutoConnect"] = menu.AddToggle("Auto Connect", "Automatically connect to the AltNetIK server", (value) =>
            {
                autoConnect = value;
                MelonPreferences.SetEntryValue(ModID, "AutoConnect", value);
            }, autoConnect);

            buttons["Ping"] = menu.AddButton("Ping\n" + serverPeer?.RoundTripTime, "Current ping to AltNetIk server.", () => { });
            buttons["Ping"].Interactable = false;

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

            if (autoConnect)
                MelonCoroutines.Start(Connect());
        }

        public override void OnUpdate()
        {
            client?.PollEvents();

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
        }

        public override void OnApplicationQuit()
        {
            DisconnectSilent();
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
            enableLerp = !enableLerp;
            UpdateAllButtons();
        }

        public override void OnEnterWorld(ApiWorld world, ApiWorldInstance instance)
        {
            ResetInstance();

            IsSending = false;
            string instanceId = $"{world.id}:{instance.id}";
            HashAlgorithm algorithm = new MD5CryptoServiceProvider();
            byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(instanceId));
            currentInstanceIdHash = Convert.ToBase64String(hash);
        }

        public override void OnLeftRoom()
        {
            ResetInstance();
        }

        private void ResetInstance()
        {
            currentInstanceIdHash = String.Empty;
            currentPhotonId = 0;
            receiverPacketData.Clear();
            receiverLastPacket.Clear();
            frozenPlayers.Clear();
            receiverPlayerData.Clear();
            playerNamePlates.Clear();
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
                playerNamePlates.Remove(photonId);
                receiverPlayerData.TryRemove(photonId, out _);
                receiverPacketData.TryRemove(photonId, out _);
                receiverLastPacket.TryRemove(photonId, out _);
                RemoveFreeze(photonId);
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
            UpdateButtonText("Ping", "Ping\n" + ping);
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

            UpdatePing();
        }
    }
}