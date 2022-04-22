using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UIExpansionKit.API;
using Object = UnityEngine.Object;
using Delegate = Il2CppSystem.Delegate;
using VRC.Core;
using System.Linq;
using UnityEngine.UI;
using VRC;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using TMPro;
using LiteNetLib;
using LiteNetLib.Utils;
using VRC.Playables;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Net;
using System.IO;
using ReMod.Core;
using ReMod.Core.Managers;
using ReMod.Core.UI.QuickMenu;
using ReMod.Core.VRChat;
using VRC.Networking;
using UnhollowerBaseLib;

namespace AltNetIk
{
    public class AltNetIk : ModComponent
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

        public struct NamePlateInfo
        {
            public int photonId;
            public Player player;
            public GameObject namePlate;
            public TextMeshProUGUI namePlateText;
        }

        public struct PlayerData
        {
            public int photonId;
            public bool loading;
            public bool frozen;
            public Transform playerTransform;
            public PoseRecorder playerPoseRecorder;
            public HandGestureController playerHandGestureController;
            public FlatBufferNetworkSerializer playerAnimControlNetSerializer;
            public VRCAvatarManager playerAvatarManager;
            public Quaternion[] preQArray;
            public Quaternion[] preQinvArray;
            public Quaternion[] postQArray;
            public Quaternion[] postQinvArray;
            public Quaternion hipRotationOffset;
            public Transform[] transforms;
            public int boneCount;
            public bool[] boneList;
            public Dictionary<string, AvatarParameter> parameters;
            public bool active;
        }

        public struct ReceiverPacketData
        {
            public int photonId;
            public bool loading;
            public bool frozen;
            public int boneCount;
            public bool[] boneList;
            public DataBank dataBank0;
            public DataBank dataBank1;
            public short bankSelector;

            public int packetsPerSecond;
            public int updatesPerSecond;
            public Int64 lastTimeReceived;
        }

        public struct DataBank
        {
            public Int64 timestamp;
            public PacketData.Quaternion[] boneRotations;
            public PacketData.Vector3 hipPosition;
            public PacketData.Vector3 playerPosition;
            public PacketData.Quaternion playerRotation;
        }

        private static string currentInstanceIdHash;
        private static int currentPhotonId = 0;

        private static ConcurrentDictionary<int, ReceiverPacketData> receiverPacketData = new ConcurrentDictionary<int, ReceiverPacketData>();
        private static PlayerData senderPlayerData = new PlayerData();

        private static PacketData.Quaternion[] netRotations;
        private static PacketData senderPacketData = new PacketData();
        private static ParamData senderParamData = new ParamData();

        private static List<string> boneNames = new List<string>();

        public static Dictionary<string, ReMenuButton> buttons = new Dictionary<string, ReMenuButton>();
        public static string color(string c, string s) { return $"<color={c}>{s}</color>"; } // stolen from MintLily

        private static bool autoConnect;
        private static bool autoDisconnect;
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

        public AltNetIk()
        {
            Logger = new MelonLogger.Instance(BuildInfo.Name, ConsoleColor.Magenta);
            Instance = this;

            MelonPreferences.CreateCategory(ModID, ModID);
            MelonPreferences.CreateEntry(ModID, "AutoConnect", false, "Auto connect to server on startup");
            MelonPreferences.CreateEntry(ModID, "AutoDisconnect", false, "Auto disconnect when leaving world");
            MelonPreferences.CreateEntry(ModID, "DisableLerp", false, "Disable receiver interpolation");
            MelonPreferences.CreateEntry(ModID, "ServerIP", "", "Server IP");
            MelonPreferences.CreateEntry(ModID, "ServerPort", 9050, "Server Port");
            autoConnect = MelonPreferences.GetEntryValue<bool>(ModID, "AutoConnect");
            autoDisconnect = MelonPreferences.GetEntryValue<bool>(ModID, "AutoDisconnect");
            disableLerp = MelonPreferences.GetEntryValue<bool>(ModID, "DisableLerp");
            serverIP = MelonPreferences.GetEntryValue<string>(ModID, "ServerIP");
            serverPort = MelonPreferences.GetEntryValue<int>(ModID, "ServerPort");
            Camera.onPreRender = Delegate.Combine(Camera.onPreRender, (Camera.CameraCallback)OnVeryLateUpdate).Cast<Camera.CameraCallback>();

            boneNames = HumanTrait.BoneName.ToList();

            netPacketProcessor.RegisterNestedType<PacketData.Quaternion>();
            netPacketProcessor.RegisterNestedType<PacketData.Vector3>();
            netPacketProcessor.Subscribe(OnEventPacketReceived, () => new EventData());
            netPacketProcessor.Subscribe(OnPacketReceived, () => new PacketData());
            netPacketProcessor.Subscribe(OnParamPacketReceived, () => new ParamData());

            Patches.DoPatches();
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

            //unsafe
            //         {
            //	_boolPropertySetterDelegate = DelegateCreator<BoolPropertySetterDelegate, bool>("NativeMethodInfoPtr_Method_Public_Virtual_Final_New_set_Void_Boolean_0", BoolPropertySetter);
            //             _intPropertySetterDelegate = DelegateCreator<IntPropertySetterDelegate, int>("NativeMethodInfoPtr_Method_Public_Virtual_Final_New_set_Void_Int32_0", IntPropertySetter);
            //	_floatPropertySetterDelegate = DelegateCreator<FloatPropertySetterDelegate, float>("NativeMethodInfoPtr_Method_Public_Virtual_Final_New_set_Void_Single_0", FloatPropertySetter);
            //         }

            if (autoConnect)
                MelonCoroutines.Start(Connect());

            var playerManager = PlayerManager.field_Private_Static_PlayerManager_0;
            if (playerManager != null)
            {
                foreach(var player in playerManager.GetPlayers())
                {
                    if (player == null)
                        continue;

                    var vrcPlayer = player._vrcplayer;
                    if (vrcPlayer == null)
                        continue;

                    if (vrcPlayer == VRCPlayer.field_Internal_Static_VRCPlayer_0)
                        SetSenderBones(vrcPlayer, vrcPlayer.field_Private_VRCAvatarManager_0, vrcPlayer.GetAvatarObject());
                    else
                        SetReceiverBones(vrcPlayer, vrcPlayer.field_Private_VRCAvatarManager_0);
                }
            }
        }

        private static unsafe T DelegateCreator<T, T2>(string name, Action<IntPtr, T2> method)
        {
            var fieldInfo = (IntPtr)typeof(AvatarParameter).GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            MelonUtils.NativeHookAttach(fieldInfo, new Action<IntPtr, T2>(method).Method.MethodHandle.GetFunctionPointer());
            return Marshal.GetDelegateForFunctionPointer<T>(*(IntPtr*)(void*)fieldInfo);
        }

        public override void OnUiManagerInit(UiManager uiManager)
        {
            var menu = uiManager.MainMenu.AddMenuPage("AltNetIK", "Settings for the AltNetIK module", ResourceManager.GetSprite("remod.cogwheel"));
            buttons["ConnectToggle"] = menu.AddButton("AltNetIk " + color("#ff0000", "Disconnected"), string.Empty,
                ConnectToggle, ResourceManager.GetSprite("remod.cogwheel"));
            buttons["ToggleSend"] = menu.AddButton("SendIK " + color("#00ff00", "Enabled"), string.Empty,
                ToggleSend, ResourceManager.GetSprite("remod.cogwheel"));
            buttons["ToggleReceive"] = menu.AddButton("ReceiveIK " + color("#00ff00", "Enabled"), string.Empty,
                ToggleReceive, ResourceManager.GetSprite("remod.cogwheel"));

            hasQmUiInit = true;
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
            autoDisconnect = MelonPreferences.GetEntryValue<bool>(ModID, "AutoDisconnect");
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

        private IEnumerator Connect()
        {
            try
            {
                listener = new EventBasedNetListener();
                client = new NetManager(listener);

                client.Start();
                client.Connect(serverIP, serverPort, "");

                listener.NetworkReceiveEvent += OnNetworkReceive;
                listener.PeerConnectedEvent += OnPeerConnected;
                listener.PeerDisconnectedEvent += OnPeerDisconnected;
            }
            catch (Exception e)
            {
                Logger.Msg("Connection Error: " + e);
                IsConnected = false;
                if (client != null)
                {
                    client.DisconnectAll();
                    client.Stop();
                    client = null;
                }
                DisableReceivers();
            }
            UpdateAllButtons();
            yield break;
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            netPacketProcessor.ReadAllPackets(reader);
            reader.Recycle();
        }

        private void OnPeerConnected(NetPeer peer)
        {
            serverPeer = peer;
            IsConnected = true;
            ReconnectTimer = 1000;
            ReconnectLastAttempt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Logger.Msg("Connected");
            MelonCoroutines.Start(SendLocationData());
            UpdateAllButtons();
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Logger.Msg("Server Disconnected: " + disconnectInfo.Reason);
            IsConnected = false;
            if (client != null)
            {
                client.DisconnectAll();
                client.Stop();
                client = null;
            }
            DisableReceivers();
            UpdateAllButtons();
        }

        private void AutoReconnect()
        {
            if (IsConnected || ReconnectLastAttempt == 0)
                return;

            var date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (date - ReconnectLastAttempt >= ReconnectTimer)
            {
                ReconnectTimer *= 2;
                if (ReconnectTimer > 3600000)
                    ReconnectTimer = 3600000; // 1 hour max
                ReconnectLastAttempt = date;
                Logger.Msg("Attempting to reconnect");
                MelonCoroutines.Start(Connect());
            }
        }

        public static void OnPacketReceived(PacketData packet)
        {
            if (!IsReceiving)
                return;

            var date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            DataBank dataBank = new DataBank
            {
                timestamp = date,
                boneRotations = packet.boneRotations,
                hipPosition = packet.hipPosition,
                playerPosition = packet.playerPosition,
                playerRotation = packet.playerRotation,
            };
            bool hasPacketData = receiverPacketData.TryGetValue(packet.photonId, out ReceiverPacketData packetData);
            if (hasPacketData)
            {
                packetData.packetsPerSecond++;
                packetData.photonId = packet.photonId;
                packetData.boneList = packet.boneList;
                packetData.loading = packet.loading;
                if (packetData.frozen != packet.frozen)
                {
                    packetData.frozen = packet.frozen;
                    ToggleFreeze(packet.photonId, packet.frozen);
                }
                if (date != packetData.lastTimeReceived)
                {
                    packetData.updatesPerSecond++;
                    packetData.lastTimeReceived = date;
                }
                int boneCount = packet.boneCount;
                if (packetData.boneCount != boneCount)
                {
                    packetData.boneCount = boneCount;
                    packetData.dataBank0 = dataBank;
                    packetData.dataBank1 = dataBank;
                    //Logger.Msg("boneCount missmatch");
                }

                if (packetData.bankSelector == 0)
                {
                    packetData.bankSelector = 1;
                    packetData.dataBank1 = dataBank;
                }
                else
                {
                    packetData.bankSelector = 0;
                    packetData.dataBank0 = dataBank;
                }

                receiverPacketData.AddOrUpdate(packet.photonId, packetData, (k, v) => packetData);
            }
            else
            {
                ReceiverPacketData newPacketData = new ReceiverPacketData
                {
                    photonId = packet.photonId,
                    loading = packet.loading,
                    frozen = packet.frozen,
                    boneCount = packet.boneCount,
                    boneList = packet.boneList,
                    dataBank0 = dataBank,
                    dataBank1 = dataBank,
                    bankSelector = 0,
                    packetsPerSecond = 1,
                    updatesPerSecond = 1,
                    lastTimeReceived = date
                };
                receiverPacketData.AddOrUpdate(packet.photonId, newPacketData, (k, v) => newPacketData);
                if (packet.frozen)
                    ToggleFreeze(packet.photonId, packet.frozen);
            }
        }

        private static void ToggleFreeze(int photonId, bool frozen)
        {
            bool hasBoneData = receiverPlayerData.TryGetValue(photonId, out PlayerData boneData);
            if (!hasBoneData)
                return;

            RemoveFreeze(photonId);
            if (frozen)
            {
                var avatarTransfrom = boneData.playerTransform.Find("ForwardDirection/Avatar");
                if (avatarTransfrom == null)
                    return;
                GameObject avatar = avatarTransfrom.gameObject;
                GameObject avatarClone = Object.Instantiate(avatar);
                foreach (Component component in avatarClone.GetComponents<Component>())
                    if (!(component is Transform))
                        Object.Destroy(component);
                avatarClone.transform.SetPositionAndRotation(boneData.playerTransform.position, boneData.playerTransform.rotation);
                frozenPlayers.Add(photonId, avatarClone);
            }
        }

        private static void RemoveFreeze(int photonId)
        {
            bool hasFrozenPlayer = frozenPlayers.TryGetValue(photonId, out GameObject frozenPlayer);
            if (hasFrozenPlayer)
            {
                if (frozenPlayer != null)
                    Object.Destroy(frozenPlayer);
                frozenPlayers.Remove(photonId);
            }
        }

        private void ConnectToggle()
        {
            ReconnectLastAttempt = 0;
            if (IsConnected)
                Disconnect();
            else
                MelonCoroutines.Start(Connect());
        }

        private void Disconnect()
        {
            IsConnected = false;
            ReconnectLastAttempt = 0;
            if (client != null)
            {
                client.DisconnectAll();
                client.Stop();
                client = null;
                Logger.Msg("Disconnected");
            }
            DisableReceivers();
            UpdateAllButtons();
        }

        private void ToggleSend()
        {
            IsSending = !IsSending;
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

        private void DisableReceivers()
        {
            foreach (ReceiverPacketData packetData in receiverPacketData.Values)
            {
                receiverPacketData.TryRemove(packetData.photonId, out _);
                bool hasReceiverPlayerData = receiverPlayerData.TryGetValue(packetData.photonId, out PlayerData playerData);
                if (playerData.playerTransform == null)
                    continue;
                if (hasReceiverPlayerData)
                {
                    playerData.active = false;
                    playerData.playerPoseRecorder.enabled = true;
                    playerData.playerHandGestureController.enabled = true;
                    playerData.playerAnimControlNetSerializer.enabled = true;
                }
                bool hasPlayerNamePlate = PlayerNamePlates.TryGetValue(packetData.photonId, out NamePlateInfo namePlateInfo);
                if (hasPlayerNamePlate)
                {
                    namePlateInfo.namePlate.SetActive(false);
                }
                RemoveFreeze(packetData.photonId);
            }
            UpdateNamePlates();
        }

        private void ToggleLerp()
        {
            disableLerp = !disableLerp;
            UpdateAllButtons();
        }

        public void OnInstanceChanged(ApiWorld world, ApiWorldInstance instance)
        {
            ResetInstance();
            if (autoDisconnect)
                Disconnect();

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
            frozenPlayers.Clear();
            receiverPlayerData.Clear();
            PlayerNamePlates.Clear();
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
                PlayerNamePlates.Remove(photonId);
                receiverPlayerData.TryRemove(photonId, out _);
                receiverPacketData.TryRemove(photonId, out _);
                RemoveFreeze(photonId);
            }
        }

        public void OnAvatarChange(VRCAvatarManager avatarManager, ApiAvatar __1, GameObject gameObject)
        {
            VRCPlayer player = avatarManager.field_Private_VRCPlayer_0;
            if (player == null) return;

            if (player == VRCPlayer.field_Internal_Static_VRCPlayer_0)
                SetSenderBones(player, avatarManager, gameObject);
            else
                SetReceiverBones(player, avatarManager);
        }

        public void OnAvatarInit(VRCAvatarManager avatarManager, GameObject __1)
        {
            VRCPlayer player = avatarManager.field_Private_VRCPlayer_0;
            if (player == null) return;

            if (player == VRCPlayer.field_Internal_Static_VRCPlayer_0)
                senderPlayerData.loading = true;
        }

        public void SetReceiverBones(VRCPlayer player, VRCAvatarManager avatarManager)
        {
            int boneCount = 0;
            bool[] boneList = new bool[55];

            int photonId = player.prop_PhotonView_0.field_Private_Int32_0;

            var avatarParams = avatarManager.field_Private_AvatarPlayableController_0?.field_Private_Dictionary_2_Int32_AvatarParameter_0;
            var parameters = new Dictionary<string, AvatarParameter>();
            if (avatarParams != null)
                foreach (var param in avatarParams.Values)
                    parameters.Add(param.field_Private_String_0, param);

            var animationController = player.field_Private_AnimatorControllerManager_0;

            Animator animator = avatarManager.field_Private_Animator_0;
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                PlayerData emptyBoneData = new PlayerData
                {
                    photonId = photonId,
                    playerTransform = player.transform,
                    playerPoseRecorder = animationController.GetComponent<PoseRecorder>(),
                    playerHandGestureController = animationController.GetComponent<HandGestureController>(),
                    playerAnimControlNetSerializer = animationController.GetComponentInChildren<FlatBufferNetworkSerializer>(),
                    preQArray = new Quaternion[boneCount],
                    preQinvArray = new Quaternion[boneCount],
                    postQArray = new Quaternion[boneCount],
                    postQinvArray = new Quaternion[boneCount],
                    transforms = new Transform[boneCount],
                    boneCount = boneCount,
                    boneList = boneList,
                    parameters = parameters,
                    active = false
                };
                receiverPlayerData.AddOrUpdate(photonId, emptyBoneData, (k, v) => emptyBoneData);
                return;
            }

            var avatar = animator.avatar;
            var hd = avatar.humanDescription;
            var human = hd.human;
            for (int i = 0; i < human.Length; i++)
            {
                HumanBone humanBone = human[i];
                int boneIndex = boneNames.FindIndex(a => string.Equals(a, humanBone.humanName));
                if (boneIndex < 0 || humanBone.humanName == "LeftEye" || humanBone.humanName == "RightEye")
                    continue;

                boneCount++;
                boneList[boneIndex] = true;
            }
            if (boneCount == 0)
            {
                // legacy fallback
                int bodyBoneIndex = -1;
                for (int i = 0; i < 55; i++)
                {
                    HumanBodyBones bodyBone = (HumanBodyBones)i;
                    Transform bone = animator.GetBoneTransform(bodyBone);
                    if (bone == null || i == (int)HumanBodyBones.LeftEye || i == (int)HumanBodyBones.RightEye)
                        continue;

                    bodyBoneIndex++;
                    boneCount++;
                    boneList[bodyBoneIndex] = true;
                }
            }

            PlayerData boneData = new PlayerData
            {
                photonId = photonId,
                playerTransform = player.transform,
                playerPoseRecorder = animationController.GetComponent<PoseRecorder>(),
                playerHandGestureController = animationController.GetComponent<HandGestureController>(),
                playerAnimControlNetSerializer = animationController.GetComponentInChildren<FlatBufferNetworkSerializer>(),
                preQArray = new Quaternion[boneCount],
                preQinvArray = new Quaternion[boneCount],
                postQArray = new Quaternion[boneCount],
                postQinvArray = new Quaternion[boneCount],
                transforms = new Transform[boneCount],
                boneCount = boneCount,
                boneList = boneList,
                parameters = parameters,
                active = false
            };

            int index = -1;
            for (int i = 0; i < 55; i++)
            {
                if (!boneList[i])
                    continue;
                index++;

                Quaternion preQ = boneData.preQArray[index] = avatar.GetPreRotation(i);
                Quaternion postQ = boneData.postQArray[index] = avatar.GetPostRotation(i);
                boneData.preQinvArray[index] = Quaternion.Inverse(preQ);
                boneData.postQinvArray[index] = Quaternion.Inverse(postQ);
                boneData.transforms[index] = animator.GetBoneTransform((HumanBodyBones)i);
            }

            receiverPlayerData.AddOrUpdate(photonId, boneData, (k, v) => boneData);
        }

        public void SetSenderBones(VRCPlayer player, VRCAvatarManager avatarManager, GameObject gameObject)
        {
            var date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            int boneCount = 0;
            bool[] boneList = new bool[55];

            bool loading = false;
            int photonId = player.prop_PhotonView_0.field_Private_Int32_0;
            PipelineManager pipelineManager = gameObject.GetComponent<PipelineManager>();
            if (pipelineManager != null)
            {
                string avatarId = pipelineManager.blueprintId;
                if (avatarId == "avtr_749445a8-d9bf-4d48-b077-d18b776f66f7")
                {
                    loading = true;
                }
            }

            var avatarParams = avatarManager.field_Private_AvatarPlayableController_0?.field_Private_Dictionary_2_Int32_AvatarParameter_0;
            var parameters = new Dictionary<string, AvatarParameter>();
            if (avatarParams != null)
                foreach (var param in avatarParams.Values)
                    parameters.Add(param.field_Private_String_0, param);

            senderPacketData = new PacketData();
            senderParamData = new ParamData();
            Animator animator = avatarManager.field_Private_Animator_0;

            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Logger.Msg("avatar is null");
                senderPlayerData = new PlayerData
                {
                    playerTransform = player.transform,
                    playerAvatarManager = avatarManager,
                    preQArray = new Quaternion[boneCount],
                    preQinvArray = new Quaternion[boneCount],
                    postQArray = new Quaternion[boneCount],
                    postQinvArray = new Quaternion[boneCount],
                    transforms = new Transform[boneCount],
                    boneCount = boneCount,
                    boneList = boneList,
                    parameters = parameters,
                    loading = loading
                };
                return;
            }

            var avatar = animator.avatar;
            var hd = avatar.humanDescription;
            var human = hd.human;
            for (int i = 0; i < human.Length; i++)
            {
                HumanBone humanBone = human[i];
                int boneIndex = boneNames.FindIndex(a => string.Equals(a, humanBone.humanName));
                if (boneIndex < 0 || humanBone.humanName == "LeftEye" || humanBone.humanName == "RightEye")
                    continue;

                boneCount++;
                boneList[boneIndex] = true;
            }
            if (boneCount == 0)
            {
                // legacy fallback
                int bodyBoneIndex = -1;
                for (int i = 0; i < 55; i++)
                {
                    HumanBodyBones bodyBone = (HumanBodyBones)i;
                    Transform bone = animator.GetBoneTransform(bodyBone);
                    if (bone == null || i == (int)HumanBodyBones.LeftEye || i == (int)HumanBodyBones.RightEye)
                        continue;

                    bodyBoneIndex++;
                    boneCount++;
                    boneList[bodyBoneIndex] = true;
                }
            }

            senderPlayerData = new PlayerData
            {
                playerTransform = player.transform,
                playerAvatarManager = avatarManager,
                preQArray = new Quaternion[boneCount],
                preQinvArray = new Quaternion[boneCount],
                postQArray = new Quaternion[boneCount],
                postQinvArray = new Quaternion[boneCount],
                transforms = new Transform[boneCount],
                boneCount = boneCount,
                boneList = boneList,
                parameters = parameters,
                loading = loading
            };

            int index = -1;
            for (int i = 0; i < 55; i++)
            {
                if (!boneList[i])
                    continue;
                index++;

                Quaternion preQ = senderPlayerData.preQArray[index] = avatar.GetPreRotation(i);
                Quaternion postQ = senderPlayerData.postQArray[index] = avatar.GetPostRotation(i);
                senderPlayerData.preQinvArray[index] = Quaternion.Inverse(preQ);
                senderPlayerData.postQinvArray[index] = Quaternion.Inverse(postQ);
                senderPlayerData.transforms[index] = animator.GetBoneTransform((HumanBodyBones)i);
            }
        }

        public override void OnLateUpdate()
        {
            if (!IsConnected || !IsReceiving) return;

            foreach (ReceiverPacketData packetData in receiverPacketData.Values)
            {
                int photonId = packetData.photonId;

                bool hasBoneData = receiverPlayerData.TryGetValue(photonId, out PlayerData boneData);
                if (!hasBoneData)
                    continue;

                if (boneData.playerTransform == null)
                    continue;

                //if (boneData.loading)
                //    continue;

                if (!boneData.active)
                {
                    boneData.active = true;
                    boneData.playerPoseRecorder.enabled = false;
                    boneData.playerHandGestureController.enabled = false;
                    boneData.playerAnimControlNetSerializer.enabled = false;
                }

                Int64 date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                float deltaFloat;
                if (packetData.dataBank0.timestamp == packetData.dataBank1.timestamp)
                {
                    deltaFloat = 1.0f;
                }
                else if (packetData.bankSelector == 0)
                {
                    double date0 = date - packetData.dataBank0.timestamp;
                    double deltaTime = packetData.dataBank0.timestamp - packetData.dataBank1.timestamp;
                    double delta = Math.Round((deltaTime - date0) / deltaTime * 100, 2) / 100;
                    deltaFloat = (float)-delta + 1;
                }
                else
                {
                    double date1 = date - packetData.dataBank1.timestamp;
                    double deltaTime = packetData.dataBank1.timestamp - packetData.dataBank0.timestamp;
                    double delta = Math.Round((deltaTime - date1) / deltaTime * 100, 2) / 100;
                    deltaFloat = (float)-delta + 1;
                }

                // clamp delta
                // TODO avarage delta over a few frames
                if (disableLerp || deltaFloat > 1.0f)
                    deltaFloat = 1.0f;

                int index = -1;
                for (int i = 0; i < 55; i++)
                {
                    if (!boneData.boneList[i])
                        continue;
                    index++;

                    if (packetData.boneCount <= index || boneData.boneCount <= index)
                        break;
                    if (!packetData.boneList[i] || !boneData.transforms[index])
                        continue;

                    Quaternion boneRotation;
                    if (packetData.bankSelector == 0)
                    {
                        boneRotation = LerpUnclamped(packetData.dataBank1.boneRotations[index], packetData.dataBank0.boneRotations[index], deltaFloat);
                    }
                    else
                    {
                        boneRotation = LerpUnclamped(packetData.dataBank0.boneRotations[index], packetData.dataBank1.boneRotations[index], deltaFloat);
                    }

                    //if (i == 0)
                    //{
                    //    boneData.transforms[index].rotation = boneData.preQArray[index] * boneRotation * boneData.postQinvArray[index];
                    //    continue;
                    //}
                    boneData.transforms[index].localRotation = boneData.preQArray[index] * boneRotation * boneData.postQinvArray[index];
                }

                if (packetData.bankSelector == 0)
                {
                    Quaternion playerRotation = LerpUnclamped(packetData.dataBank1.playerRotation, packetData.dataBank0.playerRotation, deltaFloat);
                    Vector3 playerPosition = LerpUnclamped(packetData.dataBank1.playerPosition, packetData.dataBank0.playerPosition, deltaFloat);
                    Vector3 hipPosition = LerpUnclamped(packetData.dataBank1.hipPosition, packetData.dataBank0.hipPosition, deltaFloat);
                    boneData.playerTransform.SetPositionAndRotation(playerPosition, playerRotation);
                    if (boneData.transforms.Length > 0 && boneData.transforms[0] != null)
                    {
                        boneData.transforms[0].position = hipPosition;
                    }
                }
                else
                {
                    Quaternion playerRotation = LerpUnclamped(packetData.dataBank0.playerRotation, packetData.dataBank1.playerRotation, deltaFloat);
                    Vector3 playerPosition = LerpUnclamped(packetData.dataBank0.playerPosition, packetData.dataBank1.playerPosition, deltaFloat);
                    Vector3 hipPosition = LerpUnclamped(packetData.dataBank0.hipPosition, packetData.dataBank1.hipPosition, deltaFloat);
                    boneData.playerTransform.SetPositionAndRotation(playerPosition, playerRotation);
                    if (boneData.transforms.Length > 0 && boneData.transforms[0] != null)
                    {
                        boneData.transforms[0].position = hipPosition;
                    }
                }
            }
        }

        public void OnVeryLateUpdate(Camera camera)
        {
            if (camera != Camera.main ||
                !IsConnected ||
                !IsSending ||
                currentPhotonId == 0 ||
                senderPlayerData.playerTransform == null)
            {
                return;
            }

            netRotations = new PacketData.Quaternion[senderPlayerData.boneCount];

            int index = -1;
            for (int i = 0; i < 55; i++)
            {
                if (!senderPlayerData.boneList[i])
                    continue;
                index++;

                if (!senderPlayerData.transforms[index])
                    continue;

                //if (i == 0)
                //{
                //    netRotations[index] = senderPlayerData.preQinvArray[index] * senderPlayerData.transforms[index].rotation * senderPlayerData.postQArray[index];
                //    continue;
                //}
                netRotations[index] = senderPlayerData.preQinvArray[index] * senderPlayerData.transforms[index].localRotation * senderPlayerData.postQArray[index];
            }

            senderPacketData.boneRotations = netRotations;
            senderPacketData.boneList = senderPlayerData.boneList;
            senderPacketData.boneCount = (byte)senderPlayerData.boneCount;
            senderPacketData.playerPosition = senderPlayerData.playerTransform.position;
            senderPacketData.playerRotation = senderPlayerData.playerTransform.rotation;
            senderPacketData.photonId = currentPhotonId;
            senderPacketData.loading = senderPlayerData.loading;
            senderPacketData.frozen = IsFrozen;

            if (senderPlayerData.transforms.Length > 0 && senderPlayerData.transforms[0] != null)
            {
                senderPacketData.hipPosition = senderPlayerData.transforms[0].position;
            }

            MelonCoroutines.Start(SendData());

            MelonCoroutines.Start(SendParamData());
        }

        public IEnumerator SendData()
        {
            if (serverPeer == null) yield break;
            NetDataWriter writer = new NetDataWriter();
            netPacketProcessor.Write(writer, senderPacketData);
            if (writer.Length > serverPeer.Mtu)
                Logger.Error($"IK packet too large {writer.Length}/{serverPeer.Mtu}");
            serverPeer.Send(writer, DeliveryMethod.Sequenced);

            yield break;
        }

        public static IEnumerator SendParamData()
        {
            if (serverPeer == null) yield break;

            int paramCount = senderPlayerData.parameters.Count;
            if (senderParamData?.paramName?.Length != paramCount)
            {
                senderParamData.paramName = new string[paramCount];
                senderParamData.paramType = new short[paramCount];
                senderParamData.paramValue = new float[paramCount];
            }
            if (paramCount == 0)
                yield break;

            int i = 0;
            foreach (var parameter in senderPlayerData.parameters.Values)
            {
                float value;
                var type = parameter.field_Private_ParameterType_0;
                switch (type)
                {
                    case AvatarParameter.ParameterType.Bool:
                        value = parameter.field_Private_Boolean_0 ? 1.0f : 0.0f;
                        break;
                    case AvatarParameter.ParameterType.Int:
                        value = parameter.field_Private_Int32_1;
                        break;
                    case AvatarParameter.ParameterType.Float:
                        value = parameter.field_Private_Single_0;
                        break;
                    default:
                        value = 0f;
                        break;
                }
                senderParamData.paramName[i] = parameter.field_Private_String_0;
                senderParamData.paramType[i] = (short)type;
                senderParamData.paramValue[i] = value;
                i++;
            }
            senderParamData.photonId = currentPhotonId;

            NetDataWriter writer = new NetDataWriter();
            netPacketProcessor.Write(writer, senderParamData);
            serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);

            yield break;
        }

        public IEnumerator SendLocationData()
        {
            if (serverPeer == null) yield break;
            NetDataWriter writer = new NetDataWriter();
            EventData eventData = new EventData
            {
                lobbyHash = currentInstanceIdHash,
                photonId = currentPhotonId,
                eventName = "LocationUpdate"
            };
            netPacketProcessor.Write(writer, eventData);
            serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);

            yield break;
        }

        public void SetNamePlate(int photonId, Player player)
        {
            // stolen from ReModCE
            Transform stats = Object.Instantiate(player.gameObject.transform.Find("Player Nameplate/Canvas/Nameplate/Contents/Quick Stats"), player.gameObject.transform.Find("Player Nameplate/Canvas/Nameplate/Contents"));
            if (stats == null)
            {
                Logger.Error("Couldn't find nameplate");
                return;
            }
            stats.localPosition = new Vector3(0f, -60f, 0f);
            stats.transform.localScale = new Vector3(1f, 1f, 2f);
            stats.parent = player.gameObject.transform.Find("Player Nameplate/Canvas/Nameplate/Contents");
            stats.gameObject.SetActive(true);
            TextMeshProUGUI namePlate = stats.Find("Trust Text").GetComponent<TextMeshProUGUI>();
            namePlate.color = Color.white;
            namePlate.text = "init";
            namePlate.enabled = false;
            NamePlateInfo namePlateInfo = new NamePlateInfo
            {
                photonId = photonId,
                player = player,
                namePlate = stats.gameObject,
                namePlateText = namePlate
            };
            PlayerNamePlates.Add(photonId, namePlateInfo);
            stats.Find("Trust Icon").gameObject.SetActive(false);
            stats.Find("Performance Icon").gameObject.SetActive(false);
            stats.Find("Performance Text").gameObject.SetActive(false);
            stats.Find("Friend Anchor Stats").gameObject.SetActive(false);
        }

        private void TimeoutCheck()
        {
            var date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (ReceiverPacketData packetData in receiverPacketData.Values)
            {
                if (packetData.lastTimeReceived > 0 && date - packetData.lastTimeReceived >= 3000)
                {
                    //player connection died
                    receiverPacketData.TryRemove(packetData.photonId, out _);
                    bool hasReceiverPlayerData = receiverPlayerData.TryGetValue(packetData.photonId, out PlayerData playerData);
                    if (playerData.playerTransform == null)
                        continue;
                    if (hasReceiverPlayerData)
                    {
                        playerData.active = false;
                        playerData.playerPoseRecorder.enabled = true;
                        playerData.playerHandGestureController.enabled = true;
                        playerData.playerAnimControlNetSerializer.enabled = true;
                    }
                    bool hasPlayerNamePlate = PlayerNamePlates.TryGetValue(packetData.photonId, out NamePlateInfo namePlateInfo);
                    if (hasPlayerNamePlate)
                    {
                        namePlateInfo.namePlate.SetActive(false);
                    }
                    RemoveFreeze(packetData.photonId);
                }
            }
        }

        private void UpdateNamePlates()
        {
            foreach (NamePlateInfo namePlateInfo in PlayerNamePlates.Values.ToList())
            {
                if (!namePlateInfo.namePlate)
                {
                    PlayerNamePlates.Remove(namePlateInfo.photonId);
                    continue;
                }

                bool hasPacketData = receiverPacketData.TryGetValue(namePlateInfo.photonId, out ReceiverPacketData packetData);
                if (!hasPacketData)
                {
                    namePlateInfo.namePlate.SetActive(false);
                    continue;
                }

                namePlateInfo.namePlate.SetActive(true);
                namePlateInfo.namePlateText.enabled = true;
                if (packetData.packetsPerSecond == 0)
                    namePlateInfo.namePlateText.text = $"{color("#ff0000", "Disconnected")}";
                else
                {
                    string loadingText = String.Empty;
                    string frozenText = String.Empty;
                    if (packetData.loading)
                        loadingText = $" {color("#00ff00", "Loading")}";
                    if (packetData.frozen)
                        frozenText = $" {color("#ff0000", "Frozen")}";
                    namePlateInfo.namePlateText.text = $"PPS: {packetData.packetsPerSecond * 2} UPS: {packetData.updatesPerSecond * 2}{loadingText}{frozenText}";
                }

                packetData.packetsPerSecond = 0;
                packetData.updatesPerSecond = 0;
                receiverPacketData.AddOrUpdate(namePlateInfo.photonId, packetData, (k, v) => packetData);
            }
        }

        public static void UpdateButtonText(string buttonName, string text)
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

        public static void UpdateAllButtons()
        {
            if (!hasQmUiInit)
                return;

            if (IsConnected)
                UpdateButtonText("ConnectToggle", "AltNetIk " + color("#00ff00", "Connected"));
            else
                UpdateButtonText("ConnectToggle", "AltNetIk " + color("#ff0000", "Disconnected"));

            if (IsSending)
                UpdateButtonText("ToggleSend", "SendIK " + color("#00ff00", "Enabled"));
            else
                UpdateButtonText("ToggleSend", "SendIK " + color("#ff0000", "Disabled"));

            if (IsReceiving)
                UpdateButtonText("ToggleReceive", "ReceiveIK " + color("#00ff00", "Enabled"));
            else
                UpdateButtonText("ToggleReceive", "ReceiveIK " + color("#ff0000", "Disabled"));
        }

        public static void OnParamPacketReceived(ParamData packet)
        {
            if (!IsReceiving)
                return;

            bool hasPlayerData = receiverPlayerData.TryGetValue(packet.photonId, out PlayerData playerData);
            if (!hasPlayerData)
                return;

            for (int i = 0; i < packet.paramName.Length; i++)
            {
                bool hasParamName = playerData.parameters.TryGetValue(packet.paramName[i], out AvatarParameter parameter);
                if (!hasParamName || parameter == null)
                    continue;

                var type = parameter.field_Private_ParameterType_0;
                var senderType = (AvatarParameter.ParameterType)packet.paramType[i];
                if (type != senderType)
                    continue;

                float newValue = packet.paramValue[i];
                switch (type)
                {
                    case AvatarParameter.ParameterType.Bool:
                        _boolPropertySetterDelegate(parameter.Pointer, newValue == 1.0f);
                        break;
                    case AvatarParameter.ParameterType.Int:
                        _intPropertySetterDelegate(parameter.Pointer, (int)newValue);
                        break;
                    case AvatarParameter.ParameterType.Float:
                        _floatPropertySetterDelegate(parameter.Pointer, newValue);
                        break;
                }

                // Fix avatar limb grabber
                if (packet.paramName[i] == "GestureLeft")
                {
                    if ((int)newValue == 1)
                    {
                        if (playerData.playerHandGestureController.field_Private_Gesture_0 != HandGestureController.Gesture.Fist)
                            playerData.playerHandGestureController.field_Private_Gesture_0 = HandGestureController.Gesture.Fist;
                    }
                    else
                    {
                        if (playerData.playerHandGestureController.field_Private_Gesture_0 == HandGestureController.Gesture.Fist)
                            playerData.playerHandGestureController.field_Private_Gesture_0 = HandGestureController.Gesture.None;
                    }
                }
                else if (packet.paramName[i] == "GestureRight")
                {
                    if ((int)newValue == 1)
                    {
                        if (playerData.playerHandGestureController.field_Private_Gesture_2 != HandGestureController.Gesture.Fist)
                            playerData.playerHandGestureController.field_Private_Gesture_2 = HandGestureController.Gesture.Fist;
                    }
                    else
                    {
                        if (playerData.playerHandGestureController.field_Private_Gesture_2 == HandGestureController.Gesture.Fist)
                            playerData.playerHandGestureController.field_Private_Gesture_2 = HandGestureController.Gesture.None;
                    }
                }
            }
        }

        public static void OnEventPacketReceived(EventData packet)
        {
            switch (packet.eventName)
            {
                case "DisableSender":
                    IsSending = false;
                    break;
                case "EnableSender":
                    IsSending = true;
                    break;
            }
            UpdateAllButtons();
        }

        public static Quaternion LerpUnclamped(PacketData.Quaternion q1, PacketData.Quaternion q2, float t)
        {
            Quaternion r = new Quaternion
            {
                x = q1.x + t * (q2.x - q1.x),
                y = q1.y + t * (q2.y - q1.y),
                z = q1.z + t * (q2.z - q1.z),
                w = q1.w + t * (q2.w - q1.w)
            };
            r.Normalize();
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
    }
}
