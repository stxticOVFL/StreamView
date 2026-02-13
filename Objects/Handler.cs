using I2.Loc;
using MelonLoader;
using MelonLoader.TinyJSON;
using NeonLite.Modules;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using static StreamView.OBSInfo;

namespace StreamView.Objects
{
    internal class Handler : IModule
    {
        internal static bool active = true;
        const bool priority = true;

        internal static MelonPreferences_Entry<string> idName;
        internal static MelonPreferences_Entry<string> displayName;
        internal static MelonPreferences_Entry<Color> winColor;
        internal static MelonPreferences_Entry<Color> defaultColor;
        internal static MelonPreferences_Entry<string> nameFont;
        internal static MelonPreferences_Entry<int> nameFontM;
        internal static MelonPreferences_Entry<float> nameFontS;


        static void Setup()
        {
            idName = NeonLite.Settings.Add(Settings.h, "", "idName", "ID", "The ID that the hosters will use to select your POV.\n*Keep this short, simple, and unique. Case insensitive.*", "DEFAULT");
            displayName = NeonLite.Settings.Add(Settings.h, "", "displayName", "Display Name", "Your name that'll display on stream.", "");
            winColor = NeonLite.Settings.Add(Settings.h, "", "winColor", "Win Color", "The color to display for when you're ahead.\n**Either keep this default or change it to a similar, READABLE color. Don't be stupid.**", new Color(1, 0.859f, 0.498f));
            defaultColor = NeonLite.Settings.Add(Settings.h, "", "defaultColor", "Default Color", "**DO NOT TOUCH THIS UNLESS TOLD BY TOURNAMENT.**", Color.white, true);
            nameFont = NeonLite.Settings.Add(Settings.h, "", "nameFont", "Name Font", "**DO NOT TOUCH THIS UNLESS TOLD BY TOURNAMENT.**\nUsed to calculate name font size.", "fonts/RIFTON-CAPS-ITALIC SDF", true);
            nameFontM = NeonLite.Settings.Add(Settings.h, "", "nameFontM", "Name Font Max", "**DO NOT TOUCH THIS UNLESS TOLD BY TOURNAMENT.**\nUsed to calculate name font size.", 96, true);
            nameFontS = NeonLite.Settings.Add(Settings.h, "", "nameFontS", "Name Font Sensitivity", "**DO NOT TOUCH THIS UNLESS TOLD BY TOURNAMENT.**\nUsed to calculate name font size.", 8.4f, true);
        }

        internal static void Activate(bool activate)
        {
            if (activate)
                Singleton<Game>.Instance.winAction += OnLevelWin;
            else
                Singleton<Game>.Instance.winAction -= OnLevelWin;

            Patching.TogglePatch(activate, typeof(NeonNetwork.Online.Rooms), "CheckRaceFinish", SetRaceTimeCRF, Patching.PatchTarget.Postfix);
            Patching.TogglePatch(activate, typeof(NeonNetwork.Online.Rooms), "LoadRaceLevel", SetRaceTimeLRL, Patching.PatchTarget.Postfix);

            Patching.TogglePatch(activate, typeof(NeonNetwork.Online.Rooms), "StartRace", OnStartRace, Patching.PatchTarget.Prefix);
            Patching.TogglePatch(activate, typeof(NeonNetwork.Online.Rooms), "FinishRace", OnFinishRace, Patching.PatchTarget.Prefix);

            Patching.TogglePatch(activate, typeof(NeonNetwork.Online.Rooms), "LeaveRoom", ResetScore, Patching.PatchTarget.Prefix);

            active = activate;
        }

        internal static TextMeshPro nameChecker;

        static void SetupChecker()
        {
            if (!nameChecker)
            {
                nameChecker = new GameObject("Name Checker", typeof(TextMeshPro)).GetComponent<TextMeshPro>();
                nameChecker.transform.parent = StreamView.holder.transform;
                nameChecker.alignment = TextAlignmentOptions.Center;
                nameChecker.fontStyle = FontStyles.UpperCase;
                nameChecker.autoSizeTextContainer = false;
                nameChecker.fontSizeMin = 8;
                nameChecker.enableAutoSizing = true;
            }
            nameChecker.fontSizeMax = nameFontM.Value;
            nameChecker.font = Resources.Load<TMP_FontAsset>(nameFont.Value);
        }


        static int score = 0;

        internal static readonly Dictionary<Guid, Requests.Request> requests = [];

        // REGEXS
        internal static readonly Regex r_sceneName = new(@"^SV-(\d+)$", RegexOptions.Singleline);
        internal static readonly Regex r_groupName = new(@"^SV-ID-(?<index>\d+)(?<side>\w)-(?<id>\w+)$", RegexOptions.Singleline);

        internal static LevelData lastLevel;

        internal static void OnLevelLoad(LevelData level)
        {
            if (lastLevel == level)
                return;

            foreach (Awaiter a in Awaiter.instances)
                OnLevelLoadA(level, a);

            lastLevel = level;
        }
        internal static void OnLevelLoadA(LevelData level, Awaiter awaiter)
        {

            if (!awaiter.activeNext || awaiter.activeSide == null)
                return;

            List<Requests.Request> reqs = [];
            bool hub = !level || level.type == LevelData.LevelType.Hub;

            var blankSetting = new ProxyObject
            {
                {
                    "text", new ProxyString("")
                }
            };

            if (awaiter.justSet)
            {
                // we have priority, we set the name and bg

                if (hub)
                {
                    reqs.Add(new Requests.SetInputSettings
                    {
                        inputName = awaiter.GetInput("Level", false),
                        inputSettings = blankSetting,
                    });
                }
                else
                {
                    string lname = LocalizationManager.GetTranslation(level.GetLevelDisplayName());
                    if (string.IsNullOrEmpty(lname))
                        lname = level.levelDisplayName;

                    reqs.Add(new Requests.SetInputSettings
                    {
                        inputName = awaiter.GetInput("Level", false),
                        inputSettings = new ProxyObject
                        {
                            {
                                "text", new ProxyString(lname)
                            }
                        }
                    });

                    var bgGet = new Requests.GetInputSettings
                    {
                        inputName = awaiter.GetInput("BG", false),
                        func = r =>
                        {
                            var dir = Path.GetDirectoryName(r.inputSettings["file"]);
                            var ext = Path.GetExtension(r.inputSettings["file"]);
                            var name = level.GetPreviewImage().name;

                            var bgSet = new Requests.SetInputSettings
                            {
                                inputName = awaiter.GetInput("BG", false),
                                inputSettings = new ProxyObject
                                {
                                    {
                                        "file", new ProxyString($"{dir}{Path.DirectorySeparatorChar}{name}{ext}")
                                    }
                                }
                            };

                            awaiter.Send(RequestToPacket(bgSet));
                        }
                    };

                    if (level.GetPreviewImage())
                        reqs.Add(bgGet);
                }
            }

            if (hub)
            {
                reqs.Add(new Requests.SetInputSettings
                {
                    inputName = awaiter.GetInput("PB", false, awaiter.activeSide),
                    inputSettings = blankSetting
                });

                reqs.Add(new Requests.SetInputSettings
                {
                    inputName = awaiter.GetInput("Time", false, awaiter.activeSide),
                    inputSettings = blankSetting
                });
            }
            else
            {
                var stats = GameDataManager.GetLevelStats(level.levelID);
                if (stats != null)
                {
                    reqs.Add(new Requests.SetInputSettings
                    {
                        inputName = awaiter.GetInput("PB", false, awaiter.activeSide),
                        inputSettings = new ProxyObject
                        {
                            {
                                "text", new ProxyString("PB: " + NeonLite.Helpers.FormatTime(stats.GetTimeBestMicroseconds() / 1000, true, '.'))
                            }
                        }
                    });
                }
            }

            reqs.Add(awaiter.SetTextCompare("Score", false, score.ToString(), score, (a, b) => a > b));
            awaiter.Send(RequestsToPacket([.. reqs]));
        }

        static void OnLevelWin()
        {
            if (!lastLevel)
                return;

            foreach (Awaiter a in Awaiter.instances)
            {
                long ms = Singleton<Game>.Instance.GetCurrentLevelTimerMicroseconds();
                var stats = GameDataManager.GetLevelStats(lastLevel.levelID);

                if (stats != null && stats._timeBestMicroseconds >= ms)
                {
                    var req = new Requests.SetInputSettings
                    {
                        inputName = a.GetInput("PB", true, a.activeSide),
                        inputSettings = new ProxyObject
                    {
                        {
                            "text", new ProxyString("PB: " + NeonLite.Helpers.FormatTime(ms / 1000, true, '.'))
                        }
                    }
                    };

                    if (a.activeNext)
                    {
                        var copy = new Requests.SetInputSettings
                        {
                            inputName = a.GetInput("PB", false, a.activeSide),
                            inputSettings = req.inputSettings
                        };
                        a.Send(RequestsToPacket(req, copy));
                    }
                    else
                        a.Send(RequestToPacket(req));
                }
            }
        }

        static void OnStartRace()
        {
            foreach (var awaiter in Awaiter.instances)
            {
                if (!awaiter.activeCurrent)
                    continue;
                var req = new Requests.SetInputSettings
                {
                    inputName = awaiter.GetInput("Time", true, awaiter.activeSide),
                    inputSettings = new ProxyObject
                {
                    {
                        "text", new ProxyString($"{long.MaxValue}\n---DNF---")
                    },
                    {
                        "color", new ProxyNumber(uint.MaxValue)
                    }
                }
                };

                if (awaiter.activeNext)
                {
                    var copy = new Requests.SetInputSettings
                    {
                        inputName = awaiter.GetInput("Time", false, awaiter.activeSide),
                        inputSettings = req.inputSettings
                    };
                    awaiter.Send(RequestsToPacket(req, copy));
                }
                else
                    awaiter.Send(RequestToPacket(req));
            }
        }

        static void OnFinishRace()
        {
            var winner = NeonNetwork.Online.Rooms.inRoom.Values.Where(x => x.racing).OrderBy(user => user.racePB).FirstOrDefault();
            if (winner == null)
                return;

            if (winner.racePB < NeonNetwork.Online.Rooms.racePB)
                return;

            foreach (var awaiter in Awaiter.instances)
            {
                if (!awaiter.activeNext)
                    continue;

                score++;
                var scoreset = awaiter.SetTextCompare("Score", false, score.ToString(), score, (a, b) => a > b);

                awaiter.Send(RequestsToPacket(scoreset));
            }
        }

        static void ResetScore() => score = 0;

        static void SetRaceTime(Awaiter awaiter)
        {
            if (!awaiter.activeCurrent)
                return;

            var racePB = NeonNetwork.Online.Rooms.racePB;
            if (racePB == long.MaxValue)
                return;

            var text = NeonLite.Helpers.FormatTime(racePB / 1000, true, '.');
            var tc = awaiter.SetTextCompare("Time", true, $"{racePB}\n{text}", racePB, (a, b) => a < b);
            awaiter.Send(RequestToPacket(tc));
        }

        static void SetRaceTimeLRL()
        {
            if (NeonNetwork.Online.Rooms.isRacing)
            {
                foreach (var awaiter in Awaiter.instances)
                    SetRaceTime(awaiter);
            }
        }

        static void SetRaceTimeCRF()
        {
            if (!NeonNetwork.Online.Rooms.isRacing)
            {
                foreach (var awaiter in Awaiter.instances)
                    SetRaceTime(awaiter);
            }
        }

        internal static void HandlePacket(OBSPacket packet, Awaiter awaiter)
        {
            StreamView.Log.DebugMsg(packet.opcode);
            //StreamView.Log.DebugMsg(JSON.Dump(packet.rawData, EncodeOptions.NoTypeHints));

            if (packet.opcode == Opcodes.Event)
            {
                StreamView.Log.DebugMsg($"Recieve {packet.rawData["eventType"]}");
                if (packet.rawData.Keys.Contains("eventData"))
                    StreamView.Log.DebugMsg($"{JSON.Dump(packet.rawData["eventData"])}");

                PacketToEvent(packet.rawData)?.Handle(awaiter);
            }
            else if (packet.opcode == Opcodes.RequestResponse)
            {
                StreamView.Log.DebugMsg($"Recieve {packet.rawData["requestType"]}");
                if (packet.rawData.Keys.Contains("responseData"))
                    StreamView.Log.DebugMsg($"{JSON.Dump(packet.rawData["responseData"])}");

                var guid = Guid.Parse(packet.rawData["requestId"]);
                var response = PacketToResponse(packet.rawData);
                var req = requests.Pop(guid);
                if (!response.status.result)
                {
                    StreamView.Log.Error($"{response.GetType().Name} response failed with {response.status.StatusCode}: {response.status.comment}");
                    return;
                }

                req.Call(response);
            }
            else if (packet.opcode == Opcodes.RequestBatchResponse)
            {
                // honestly?
                // just do this it's easier
                var reqs = (ProxyArray)packet.rawData["results"];
                OBSPacket recur = new()
                {
                    opcode = Opcodes.RequestResponse
                };
                foreach (var req in reqs.Cast<ProxyObject>())
                {
                    recur.rawData = req;
                    HandlePacket(recur, awaiter);
                }
            }
            else
            {
                // do the fuckin start stuff here
                switch (packet.opcode)
                {
                    case Opcodes.Hello:
                        {
                            var response = new OBSPacket(Opcodes.Identify, null);
                            var data = new ProxyObject();

                            if (packet.rawData.Keys.Contains("authentication"))
                            {
                                using var sha = SHA256.Create();
                                string challenge = packet.rawData["authentication"]["challenge"];
                                string salt = packet.rawData["authentication"]["salt"];
                                // the chain is here

                                string base64 = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(Settings.password.Value + salt)));
                                data["authentication"] = new ProxyString(Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(base64 + challenge))));
                            }

                            data["rpcVersion"] = new ProxyNumber(1);
                            data["eventSubscriptions"] = new ProxyNumber((int)(EventSubscription.Inputs | EventSubscription.SceneItems | EventSubscription.Scenes));
                            response.data = data;
                            awaiter.Send(response);
                            return;
                        }
                    case Opcodes.Identified:
                        {
                            StreamView.Log.Msg("Sucessfully identified with OBS!");
                            var scene = new Requests.GetSceneList
                            {
                                func = r =>
                                {
                                    if (r.currentPreviewSceneName != null)
                                    {
                                        var m = r_sceneName.Match(r.currentPreviewSceneName);
                                        if (m.Success)
                                            awaiter.nextIndex = int.Parse(m.Groups[1].Value);
                                    }
                                    if (r.currentProgramSceneName != null)
                                    {
                                        var m = r_sceneName.Match(r.currentProgramSceneName);
                                        if (m.Success)
                                            awaiter.currentIndex = int.Parse(m.Groups[1].Value);
                                    }
                                }
                            };
                            var groups = new Requests.GetInputList
                            {
                                func = r =>
                                {
                                    bool found = false;
                                    foreach (var group in r.inputs.Select(x => x.inputName))
                                        found |= awaiter.CheckActive(group);

                                    if (found)
                                    {
                                        awaiter.justSet = true;
                                        if (!awaiter.activeNext)
                                            return;

                                        awaiter.Send(RequestsToPacket(awaiter.SetName()));
                                    }
                                }
                            };

                            SetupChecker();
                            awaiter.Send(RequestsToPacket(scene, groups));

                            return;
                        }
                }
            }
        }
    }
}
