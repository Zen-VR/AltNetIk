using MelonLoader;
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using VRC;
using Object = UnityEngine.Object;

namespace AltNetIk
{
    public partial class AltNetIk : MelonMod
    {
        public void SetNamePlate(int photonId, Player player)
        {
            // stolen from ReModCE
            Transform stats = Object.Instantiate(player.gameObject.transform.Find("Player Nameplate/Canvas/Nameplate/Contents/Quick Stats"), player.gameObject.transform.Find("Player Nameplate/Canvas/Nameplate/Contents"));
            if (stats == null)
            {
                Logger.Error("Couldn't find nameplate");
                return;
            }
            Transform statusLine = player.gameObject.transform.Find("Player Nameplate/Canvas/Nameplate/Contents/Status Line");
            Transform avatarProgress = player.gameObject.transform.Find("Player Nameplate/Canvas/Avatar Progress");
            stats.localPosition = new Vector3(0f, -58f, 0f);
            stats.transform.localScale = new Vector3(1f, 1f, 2f);
            stats.parent = player.gameObject.transform.Find("Player Nameplate/Canvas/Nameplate/Contents");
            stats.gameObject.SetActive(false);
            TextMeshProUGUI namePlate = stats.Find("Trust Text").GetComponent<TextMeshProUGUI>();
            namePlate.color = Color.white;
            namePlate.text = "init";
            namePlate.enabled = false;
            NamePlateInfo namePlateInfo = new NamePlateInfo
            {
                photonId = photonId,
                player = player,
                namePlate = stats.gameObject,
                namePlateText = namePlate,
                namePlateStatusLine = statusLine,
                namePlateAvatarProgress = avatarProgress
            };
            playerNamePlates.Add(photonId, namePlateInfo);
            stats.Find("Trust Icon").gameObject.SetActive(false);
            stats.Find("Performance Icon").gameObject.SetActive(false);
            stats.Find("Performance Text").gameObject.SetActive(false);
            stats.Find("Friend Anchor Stats").gameObject.SetActive(false);
        }

        private void UpdateNamePlates()
        {
            if (_streamSafe)
                return;

            foreach (NamePlateInfo namePlateInfo in playerNamePlates.Values.ToList())
            {
                if (!namePlateInfo.namePlate)
                {
                    playerNamePlates.Remove(namePlateInfo.photonId);
                    continue;
                }

                bool hasPacketData = receiverPacketData.TryGetValue(namePlateInfo.photonId, out ReceiverPacketData packetData);
                if (!hasPacketData || !namePlates)
                {
                    namePlateInfo.namePlate.SetActive(false);
                    namePlateInfo.namePlateStatusLine.localPosition = new Vector3(0.0066f, -58f, 0f);
                    namePlateInfo.namePlateAvatarProgress.localPosition = new Vector3(0f, -15f, 0f);
                    continue;
                }

                namePlateInfo.namePlate.SetActive(true);
                namePlateInfo.namePlateText.enabled = true;
                namePlateInfo.namePlateStatusLine.localPosition = new Vector3(0.0066f, -86f, 0f);
                namePlateInfo.namePlateAvatarProgress.localPosition = new Vector3(0f, -42f, 0f);
                if (packetData.packetsPerSecond == 0)
                    namePlateInfo.namePlateText.text = $"{color("#ff0000", "Timeout")}";
                else
                {
                    string loadingText = String.Empty;
                    string frozenText = String.Empty;
                    if (packetData.avatarKind == (short)VRCAvatarManager.AvatarKind.Loading)
                        loadingText = $" {color("#00ff00", "Loading")}";
                    if (packetData.frozen)
                        frozenText = $" {color("#ff0000", "Frozen")}";
                    namePlateInfo.namePlateText.text = $"PPS: {packetData.packetsPerSecond * 2} Ping: {packetData.ping}{loadingText}{frozenText}";
                }

                packetData.packetsPerSecond = 0;
                receiverPacketData.AddOrUpdate(namePlateInfo.photonId, packetData, (k, v) => packetData);
            }
        }
    }
}