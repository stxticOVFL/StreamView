using NeonLite;
using NeonLite.Modules;
using UnityEngine;
using StreamView.Objects;
using System.Collections.Generic;
using System.Linq;

namespace StreamView//.Objects
{
    internal class Scheduler : MonoBehaviour, IModule
    {
        static Scheduler i;

        // all this does is setup the awaiter and some patches to help out the awaiter
        internal static bool active = true;
        const bool priority = true;

        static string[] hosts;
        static readonly Dictionary<string, Awaiter> hostToAwaiter = [];

        static void Setup()
        {
            var s = NeonLite.Settings.Add(Settings.h, "", "enabled", "Enabled", null, false);
            s.Value = false;
            Handler.active = active = s.SetupForModule(Activate, (_, after) => after);

            Settings.ip = NeonLite.Settings.Add(Settings.h, "", "ip", "URLs and IPs", "This can be a *list* of URLs/IPs or just a single one.\nStreamView will constantly attempt to connect to all of them.", "ws://localhost:4455");
            Settings.password = NeonLite.Settings.Add(Settings.h, "", "password", "Password", null, "");

            Settings.ip.OnEntryValueChanged.Subscribe((_, after) => hosts = after.Split());
            hosts = Settings.ip.Value.Split();
        }

        static void Activate(bool activate)
        {
            if (!activate)
            {
                foreach (var a in Awaiter.instances)
                    a.Cancel();
                hostToAwaiter.Clear();
            }

            Handler.Activate(activate);
            active = activate;
            if (i)
                i.enabled = active;
        }

        void Awake()
        {
            i = this;
            i.enabled = active;
        }

        void Update()
        {
            foreach (var h in hosts)
            {
                if (string.IsNullOrWhiteSpace(h))
                    continue;
                if (!hostToAwaiter.ContainsKey(h))
                {
                    var a = gameObject.AddComponent<Awaiter>();
                    a.hostname = h;
                    hostToAwaiter.Add(h, a);
                }
            }

            foreach (var h in hostToAwaiter.Keys)
            {
                if (!hosts.Contains(h))
                {
                    var a = hostToAwaiter.Pop(h);
                    a.Cancel();
                }
            }
        }
    }
}
