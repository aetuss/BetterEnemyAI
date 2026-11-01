using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BetterEnemyAI
{
    interface ISynced
    {
        string Key { get; }
        string Serialize();
        void ApplyServer(string text);
        void ClearServer();
    }

    // A setting the server overrides for everyone connected to it. The client's own config file is never
    // written; its values come back when the client leaves the server.
    public class Synced<T> : ISynced
    {
        public readonly ConfigEntry<T> Entry;
        public event Action Changed;
        T serverValue;
        bool hasServerValue;

        public Synced(ConfigEntry<T> entry)
        {
            Entry = entry;
            entry.SettingChanged += (s, e) =>
            {
                RaiseChanged();
                ConfigSync.OnLocalChanged();
            };
            ConfigSync.Register(this);
        }

        public T Value
        {
            get { return hasServerValue ? serverValue : Entry.Value; }
        }

        public string Key
        {
            get { return Entry.Definition.Section + "/" + Entry.Definition.Key; }
        }

        public string Serialize()
        {
            return TomlTypeConverter.ConvertToString(Entry.Value, typeof(T));
        }

        public void ApplyServer(string text)
        {
            serverValue = (T)TomlTypeConverter.ConvertToValue(text, typeof(T));
            hasServerValue = true;
            RaiseChanged();
        }

        public void ClearServer()
        {
            if (!hasServerValue) return;
            hasServerValue = false;
            RaiseChanged();
        }

        void RaiseChanged()
        {
            if (Changed != null) Changed();
        }
    }

    // Server -> client settings sync over each peer's ZRpc connection, plus a hello reply so the server
    // can tell who is missing the mod or running another version. Clients without the mod never register
    // the RPC, and ZRpc silently drops unknown RPCs, so they are unaffected.
    static class ConfigSync
    {
        const string ConfigRpc = "BetterEnemyAI_Config";
        const string HelloRpc = "BetterEnemyAI_Hello";
        const float HelloTimeout = 20f;

        static readonly List<ISynced> settings = new List<ISynced>();
        static readonly HashSet<long> peersWithMod = new HashSet<long>();

        internal static void Register(ISynced setting)
        {
            settings.Add(setting);
        }

        static bool IsServer
        {
            get { return ZNet.instance != null && ZNet.instance.IsServer(); }
        }

        internal static void OnLocalChanged()
        {
            if (!IsServer) return;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                if (peer.m_rpc != null) Send(peer);
        }

        static void Send(ZNetPeer peer)
        {
            ZPackage pkg = new ZPackage();
            pkg.Write(Plugin.Version);
            pkg.Write(settings.Count);
            foreach (ISynced setting in settings)
            {
                pkg.Write(setting.Key);
                pkg.Write(setting.Serialize());
            }
            peer.m_rpc.Invoke(ConfigRpc, pkg);
        }

        // Client: take the server's values and answer with our version.
        static void OnConfig(ZRpc rpc, ZPackage pkg)
        {
            string serverVersion = pkg.ReadString();
            int count = pkg.ReadInt();
            Dictionary<string, string> values = new Dictionary<string, string>();
            for (int i = 0; i < count; i++)
            {
                string key = pkg.ReadString();
                values[key] = pkg.ReadString();
            }
            foreach (ISynced setting in settings)
            {
                string text;
                if (!values.TryGetValue(setting.Key, out text)) continue;
                try { setting.ApplyServer(text); }
                catch (Exception e) { Plugin.Log.LogWarning("Could not apply server value for " + setting.Key + ": " + e.Message); }
            }
            Plugin.Log.LogInfo("Using the server's BetterEnemyAI settings.");
            if (serverVersion != Plugin.Version)
                Warn("BetterEnemyAI version mismatch: server has " + serverVersion + ", you have " + Plugin.Version + ".");
            rpc.Invoke(HelloRpc, Plugin.Version);
        }

        // Server: a client confirmed it has the mod.
        static void OnHello(ZRpc rpc, string clientVersion)
        {
            ZNetPeer peer = FindPeer(rpc);
            if (peer == null) return;
            peersWithMod.Add(peer.m_uid);
            if (clientVersion != Plugin.Version)
                Warn(peer.m_playerName + " has BetterEnemyAI " + clientVersion + ", the server has " + Plugin.Version + ".");
        }

        static IEnumerator CheckHello(ZNetPeer peer)
        {
            yield return new WaitForSeconds(HelloTimeout);
            if (ZNet.instance == null || !ZNet.instance.GetPeers().Contains(peer)) yield break;
            if (!peersWithMod.Contains(peer.m_uid))
                Warn(peer.m_playerName + " doesn't have BetterEnemyAI installed. Enemies near them will behave like vanilla.");
        }

        static ZNetPeer FindPeer(ZRpc rpc)
        {
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                if (peer.m_rpc == rpc) return peer;
            return null;
        }

        static void Warn(string message)
        {
            Plugin.Log.LogWarning(message);
            if (MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, message);
        }

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        static class NewConnectionPatch
        {
            static void Postfix(ZNet __instance, ZNetPeer __0)
            {
                if (__instance.IsServer())
                    __0.m_rpc.Register<string>(HelloRpc, OnHello);
                else
                    __0.m_rpc.Register<ZPackage>(ConfigRpc, OnConfig);
            }
        }

        // Server: the peer has finished its handshake, so send it our settings and wait for its hello.
        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        static class PeerInfoPatch
        {
            static void Postfix(ZNet __instance, ZRpc __0)
            {
                if (!__instance.IsServer()) return;
                ZNetPeer peer = FindPeer(__0);
                if (peer == null) return;
                Send(peer);
                Plugin.Instance.StartCoroutine(CheckHello(peer));
            }
        }

        // Leaving the world: go back to our own settings.
        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        static class LeavePatch
        {
            static void Postfix()
            {
                foreach (ISynced setting in settings) setting.ClearServer();
                peersWithMod.Clear();
            }
        }
    }
}
