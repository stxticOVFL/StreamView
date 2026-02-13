using MelonLoader;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace StreamView
{
    internal static class Extensions
    {
        [Conditional("DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void DebugMsg(this MelonLogger.Instance log, string msg)
        {
            if (StreamView.DEBUG)
            {
                log.Msg(msg);
                UnityEngine.Debug.Log($"[NeonLite] {msg}");
            }
        }

        [Conditional("DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void DebugMsg(this MelonLogger.Instance log, object obj) => DebugMsg(log, obj.ToString());

        public static Tval Pop<Tkey, Tval>(this Dictionary<Tkey, Tval> dictionary, Tkey key)
        {
            if (dictionary.TryGetValue(key, out Tval val))
            {
                dictionary.Remove(key);
                return val;
            }
            else
                return default;
        }
    }
}
