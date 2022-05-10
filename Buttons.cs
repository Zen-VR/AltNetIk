using MelonLoader;
using ReMod.Core.Managers;
using ReMod.Core.UI.QuickMenu;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AltNetIk
{
    public static class Buttons
    {
        private static UiManager uiManager;
        public static Dictionary<string, ReMenuButton> buttons = new Dictionary<string, ReMenuButton>();
        public static Dictionary<string, ReMenuToggle> toggles = new Dictionary<string, ReMenuToggle>();

        public static void SetupButtons()
        {
            var ourAssembly = Assembly.GetExecutingAssembly();
            var resources = ourAssembly.GetManifestResourceNames();
            foreach (var resource in resources)
            {
                if (!resource.EndsWith(".png"))
                    continue;

                var stream = ourAssembly.GetManifestResourceStream(resource);

                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var resourceName = Regex.Match(resource, @"([a-zA-Z\d\-_]+)\.png").Groups[1].ToString();
                ResourceManager.LoadSprite("altnetik", resourceName, ms.ToArray());
            }

            uiManager = new UiManager("AltNetIk", ResourceManager.GetSprite("altnetik.Logo"), false);

            var menu = uiManager.MainMenu;
            buttons["ConnectToggle"] = menu.AddButton("Server\n" + AltNetIk.color("#ff0000", "Disconnected"), "Connect/Disconnect from AltNetIk server.",
                AltNetIk.Instance.ConnectToggle, ResourceManager.GetSprite("altnetik.Logo"));
            buttons["ToggleSend"] = menu.AddButton("Send\n" + AltNetIk.color("#00ff00", "Enabled"), "Toggle sending data to server (automatically managed).",
                AltNetIk.Instance.ToggleSend, ResourceManager.GetSprite("altnetik.Up"));
            buttons["ToggleReceive"] = menu.AddButton("Receive\n" + AltNetIk.color("#00ff00", "Enabled"), "Toggle receiving data from server.",
                AltNetIk.Instance.ToggleReceive, ResourceManager.GetSprite("altnetik.Down"));

            buttons["Ping"] = menu.AddButton("Ping\n" + AltNetIk.serverPeer?.RoundTripTime, "Current ping to AltNetIk server.", () => { });
            buttons["Ping"].Interactable = false;

            toggles["EnableLerp"] = menu.AddToggle("Receiver Interpolation", "Toggle receiver interpolation.", state =>
            {
                AltNetIk.enableLerp = state;
                MelonPreferences.SetEntryValue(AltNetIk.ModID, "EnableLerp", state);
            }, AltNetIk.enableLerp);
            toggles["NameplateStats"] = menu.AddToggle("Nameplate Stats", "Toggle nameplate stats.", state =>
            {
                AltNetIk.namePlates = state;
                MelonPreferences.SetEntryValue(AltNetIk.ModID, "NamePlates", state);
            }, AltNetIk.namePlates);
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
                AltNetIk.Logger.Error(e.ToString());
            }
        }

        public static void UpdateToggleState(string toggleName, bool state)
        {
            try
            {
                if (toggles.ContainsKey(toggleName) && toggles[toggleName] != null)
                    toggles[toggleName].Toggle(state);
            }
            catch (Exception e)
            {
                AltNetIk.Logger.Error(e.ToString());
            }
        }

        public static void UpdateAllButtons()
        {
            if (AltNetIk.IsConnected)
                UpdateButtonText("ConnectToggle", "Server\n" + AltNetIk.color("#00ff00", "Connected"));
            else
                UpdateButtonText("ConnectToggle", "Server\n" + AltNetIk.color("#ff0000", "Disconnected"));

            if (AltNetIk.IsSending)
                UpdateButtonText("ToggleSend", "Send\n" + AltNetIk.color("#00ff00", "Enabled"));
            else
                UpdateButtonText("ToggleSend", "Send\n" + AltNetIk.color("#ff0000", "Disabled"));

            if (AltNetIk.IsReceiving)
                UpdateButtonText("ToggleReceive", "Receive\n" + AltNetIk.color("#00ff00", "Enabled"));
            else
                UpdateButtonText("ToggleReceive", "Receive\n" + AltNetIk.color("#ff0000", "Disabled"));

            UpdateButtonText("Ping", "Ping\n" + AltNetIk.serverPeer?.RoundTripTime);
        }
    }
}