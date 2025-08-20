using MelonLoader;
using UnityEngine;

namespace StreamView
{
    public class StreamView : MelonMod
    {
        internal static StreamView instance;

#if DEBUG
        internal static bool DEBUG { get { return Settings.debug.Value; } }
#else
        internal const bool DEBUG = false;
#endif
        public static MelonLogger.Instance Log => instance.LoggerInstance;

        internal static GameObject holder;

        public override void OnInitializeMelon()
        {
            instance = this;
            Settings.Register();

#if DEBUG
            NeonLite.Modules.Anticheat.Register(MelonAssembly);
#endif
            NeonLite.NeonLite.LoadModules(MelonAssembly);

        }

        public override void OnLateInitializeMelon()
        {
            holder = new("StreamView", typeof(Scheduler));
            UnityEngine.Object.DontDestroyOnLoad(holder);
        }
    }

    public static class Settings
    {
        public const string h = "StreamView";
        public static MelonPreferences_Entry<bool> debug;

        public static MelonPreferences_Entry<string> ip;
        public static MelonPreferences_Entry<string> password;

        public static void Register()
        {
            NeonLite.Settings.AddHolder(h);
            debug = NeonLite.Settings.Add(h, "", "debug", "Debug Mode", null, false, true);
        }
    }
}
