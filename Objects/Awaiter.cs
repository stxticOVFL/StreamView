using MelonLoader.TinyJSON;
using System.Net.WebSockets;
using System.Text;
using UnityEngine;
using static StreamView.OBSInfo;

namespace StreamView.Objects
{
    internal class Awaiter : MonoBehaviour
    {
        internal readonly static List<Awaiter> instances = [];

        internal string hostname;

        ClientWebSocket ws = null;
        readonly CancellationTokenSource canceller = new();

        readonly SemaphoreSlim sendSemaphore = new(1, 1);
        readonly Queue<OBSPacket> sendQueue = [];

        async Task Connect(Uri uri)
        {
            while (!enabled)
            {
                try
                {
                    ws = new ClientWebSocket();
                    await ws.ConnectAsync(uri, CancellationToken.None);
                    StreamView.Log.Msg($"Connected to OBS! [{hostname}]");
                    enabled = true;
                }
                catch (Exception e)
                {
                    //StreamView.Log.Error($"Error connecting to OBS:");
                    //StreamView.Log.Error(e);
                    await Task.Delay(1000);
                }
            }
        }

        async void Start()
        {
            enabled = false;
            if (!Uri.TryCreate(hostname, UriKind.Absolute, out var uri))
            {
                StreamView.Log.Warning($"Invalid URL {hostname}!");
                return;
            }

            await Connect(uri);
            instances.Add(this);

            var sender = Task.Run(Sender);

            ArraySegment<byte> buffer = new(new byte[8192]);
            using var ms = new MemoryStream(8192);
            using var reader = new StreamReader(ms, Encoding.UTF8);
            Handler.requests.Clear();

            while (ws.State == WebSocketState.Open && !canceller.IsCancellationRequested)
            {
                ms.SetLength(0);

                WebSocketReceiveResult result = null;

                do
                {
                    try
                    {
                        result = await ws.ReceiveAsync(buffer, canceller.Token);
                        ms.Write(buffer.Array, buffer.Offset, result.Count);
                    }
                    catch
                    {
                        break;
                    }
                }
                while (!result.EndOfMessage && !canceller.IsCancellationRequested);
                if (canceller.IsCancellationRequested || ws.State != WebSocketState.Open)
                    break;

                reader.BaseStream.Position = 0;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message = reader.ReadToEnd();
                    try
                    {
                        Handler.HandlePacket(new(message), this);
                    }
                    catch (Exception e)
                    {
                        StreamView.Log.Error($"Error parsing packet:");
                        StreamView.Log.Error(e);
                        StreamView.Log.Warning($"Recieved from {hostname}:\n{message}");
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    StreamView.Log.Warning($"OBS [{hostname}] closed connection with {(OBSInfo.CloseCode)result.CloseStatus.Value}");
                    break;
                }
            }

            StreamView.Log.Warning($"Closing... [{hostname}]");
            await sender;
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            Destroy(this);
        }

        void OnDestroy()
        {
            instances.Remove(this);
        }

        async Task Sender()
        {
            while (ws.State == WebSocketState.Open && !canceller.IsCancellationRequested)
            {
                OBSPacket packet = null;
                await sendSemaphore.WaitAsync();
                if (sendQueue.Count > 0)
                    packet = sendQueue.Dequeue();
                sendSemaphore.Release();
                if (packet != null)
                {
                    var j = JSON.Dump(packet, EncodeOptions.NoTypeHints);
                    StreamView.Log.DebugMsg($"SENDING {packet.opcode} {JSON.Dump(packet.data, EncodeOptions.NoTypeHints)}");
                    var encoded = Encoding.UTF8.GetBytes(j);
                    var buffer = new ArraySegment<byte>(encoded, 0, encoded.Length);

                    try
                    {
                        await ws.SendAsync(buffer, WebSocketMessageType.Text, true, canceller.Token);
                    }
                    catch { }
                }
            }
        }

        internal void Send(OBSPacket packet)
        {
            sendSemaphore.Wait();
            sendQueue.Enqueue(packet);
            sendSemaphore.Release();
        }

        internal void Cancel()
        {
            if (!enabled)
                Destroy(this);
            StreamView.Log.DebugMsg($"Cancel called");
            if (!canceller.IsCancellationRequested)
                canceller.Cancel();
        }

        internal void CopyToNext()
        {
            var getScenes = new Requests.GetInputList
            {
                func = r =>
                {
                    foreach (var g in r.inputs.Select(x => x.inputName))
                    {
                        var m = Handler.r_groupName.Match(g);
                        if (!m.Success)
                            continue;

                        if (m.Groups["side"].Value == activeSide && int.Parse(m.Groups["index"].Value) == nextIndex)
                        {
                            if (m.Groups["id"].Value == Handler.idName.Value.Trim())
                            {
                                Handler.OnLevelLoadA(Handler.lastLevel, this);

                                return; // lmao oops
                            }
                            // this our target
                            var setname = new Requests.SetInputName
                            {
                                inputName = g,
                                newInputName = $"SV-ID-{nextIndex}{activeSide}-{Handler.idName.Value.Trim()}"
                            };
                            Send(RequestToPacket(setname));
                        }
                    }
                }
            };
            Send(RequestToPacket(getScenes));
        }


        internal int currentIndex = -1;
        internal int nextIndex = -1;
        internal string GetInput(string f, bool current, string side = null)
        {
            int index = current ? currentIndex : nextIndex;
            if (index == -1)
                return null;
            if (side == null)
                return $"SV-{index}-{f}";
            return $"SV-{index}{side}-{f}";
        }

        internal string activeSide = null; // also used as an indicator if we're active at all
        internal bool justSet;
        internal bool activeNext;
        internal bool activeCurrent;

        internal bool CheckActive(string group)
        {
            var m = Handler.r_groupName.Match(group);
            if (!m.Success)
                return activeSide != null;

            if (m.Groups["id"].Value.ToLowerInvariant() == Handler.idName.Value.Trim().ToLowerInvariant())
            {
                activeSide = m.Groups["side"].Value;
                justSet = true;

                var i = int.Parse(m.Groups["index"].Value);
                if (i == currentIndex)
                    activeCurrent = true;
                if (i == nextIndex)
                    activeNext = true;

                Handler.OnLevelLoadA(Handler.lastLevel, this);

                return true;
            }

            if (m.Groups["side"].Value == activeSide)
            {
                var i = int.Parse(m.Groups["index"].Value);
                if (i == currentIndex)
                    activeCurrent = false;
                if (i == nextIndex)
                    activeNext = false;

                if (!activeNext && !activeCurrent)
                    activeSide = null;
            }

            justSet = false;
            return activeSide != null;
        }

        internal Requests.GetInputSettings SetTextCompare(string input, bool current, string to, long ours, Func<long, long, bool> compare)
        {
            static uint ColorToUint(Color c)
            {
                var dColor = System.Drawing.Color.FromArgb(255, (int)(c.b * 255), (int)(c.g * 255), (int)(c.r * 255));
                return (uint)dColor.ToArgb();
            }

            var other = activeSide == "L" ? "R" : "L";
            var get = new Requests.GetInputSettings
            {
                inputName = GetInput(input, current, other),
                func = r =>
                {
                    var str = (string)(ProxyString)r.inputSettings["text"];

                    var comp = ours;
                    long.TryParse(str.Split()[0], out comp);

                    List<Requests.Request> reqs = [];
                    var ourSet = new Requests.SetInputSettings
                    {
                        inputName = GetInput(input, current, activeSide),
                        inputSettings = new ProxyObject
                        {
                            {
                                "text", new ProxyString(to)
                            },
                            {
                                "color", new ProxyNumber(ColorToUint(compare(ours, comp) ? Handler.winColor.Value : Handler.defaultColor.Value))
                            }
                        }
                    };
                    reqs.Add(ourSet);
                    if (current && activeNext)
                    {
                        // copy it to next
                        var ourSetN = new Requests.SetInputSettings
                        {
                            inputName = GetInput(input, false, activeSide),
                            inputSettings = ourSet.inputSettings
                        };
                        reqs.Add(ourSetN);
                    }

                    if (!compare(comp, ours))
                    {
                        var theirSet = new Requests.SetInputSettings
                        {
                            inputName = GetInput(input, current, other),
                            inputSettings = new ProxyObject
                            {
                                {
                                    "color", new ProxyNumber(ColorToUint(Handler.defaultColor.Value))
                                }
                            }
                        };
                        reqs.Add(theirSet);

                        if (current && activeNext)
                        {
                            // copy it to next
                            var theirSetN = new Requests.SetInputSettings
                            {
                                inputName = GetInput(input, false, other),
                                inputSettings = theirSet.inputSettings
                            };
                            reqs.Add(theirSetN);
                        }
                    }
                    Send(RequestsToPacket([.. reqs]));
                }
            };
            return get;
        }

        internal Requests.GetInputSettings SetName()
        {
            var get = new Requests.GetInputSettings
            {
                inputName = GetInput("Name", false, activeSide),
                func = r =>
                {
                    StreamView.Log.DebugMsg(JSON.Dump(r.inputSettings));

                    bool extend = r.inputSettings.Keys.Contains("extents_cx");
                    int size = 0;

                    if (extend)
                    {
                        (Handler.nameChecker.transform as RectTransform).sizeDelta = new((ProxyNumber)r.inputSettings["extents_cx"] / Handler.nameFontS.Value, 100);

                        Handler.nameChecker.enabled = false;
                        Handler.nameChecker.text = Handler.displayName.Value;
                        Handler.nameChecker.ForceMeshUpdate(true);
                        size = (int)Handler.nameChecker.fontSize;
                    }

                    var data = new ProxyObject
                    {
                        {
                            "font", r.inputSettings["font"]
                        },
                        {
                            "text", new ProxyString(Handler.displayName.Value)
                        }
                    };

                    if (extend)
                        data["font"]["size"] = new ProxyNumber(size);

                    var t = new Requests.SetInputSettings
                    {
                        inputName = GetInput("Name", false, activeSide),
                        inputSettings = data
                    };
                    Send(RequestToPacket(t));
                }
            };
            return get;
        }

    }
}
