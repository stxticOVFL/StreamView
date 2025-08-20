using MelonLoader.TinyJSON;
using StreamView.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static StreamView.OBSInfo.Events;
using static StreamView.OBSInfo.Requests;
using static StreamView.OBSInfo.Responses;

namespace StreamView
{
    public static class OBSInfo
    {
        public enum Opcodes
        {
            Hello = 0,
            Identify = 1,
            Identified = 2,
            Reidentify = 3,
            Event = 5,
            Request = 6,
            RequestResponse = 7,
            RequestBatch = 8,
            RequestBatchResponse = 9
        }

        public enum CloseCode
        {
            DontClose = 0,
            UnknownReason = 4000,
            MessageDecodeError = 4002,
            MissingDataField = 4003,
            InvalidDataFieldType = 4004,
            InvalidDataFieldValue = 4005,
            UnknownOpCode = 4006,
            NotIdentified = 4007,
            AlreadyIdentified = 4008,
            AuthenticationFailed = 4009,
            UnsupportedRpcVersion = 4010,
            SessionInvalidated = 4011,
            UnsupportedFeature = 4012
        }
        public enum RequestStatus
        {
            Unknown = 0,
            NoError = 10,
            Success = 100,
            MissingRequestType = 203,
            UnknownRequestType = 204,
            GenericError = 205,
            UnsupportedRequestBatchExecutionType = 206,
            NotReady = 207,
            MissingRequestField = 300,
            MissingRequestData = 301,
            InvalidRequestField = 400,
            InvalidRequestFieldType = 401,
            RequestFieldOutOfRange = 402,
            RequestFieldEmpty = 403,
            TooManyRequestFields = 404,
            OutputRunning = 500,
            OutputNotRunning = 501,
            OutputPaused = 502,
            OutputNotPaused = 503,
            OutputDisabled = 504,
            StudioModeActive = 505,
            StudioModeNotActive = 506,
            ResourceNotFound = 600,
            ResourceAlreadyExists = 601,
            InvalidResourceType = 602,
            NotEnoughResources = 603,
            InvalidResourceState = 604,
            InvalidInputKind = 605,
            ResourceNotConfigurable = 606,
            InvalidFilterKind = 607,
            ResourceCreationFailed = 700,
            ResourceActionFailed = 701,
            RequestProcessingFailed = 702,
            CannotAct = 703
        }

        [Flags]
        public enum EventSubscription
        {
            None = 0,
            General = (1 << 0),
            Config = (1 << 1),
            Scenes = (1 << 2),
            Inputs = (1 << 3),
            Transitions = (1 << 4),
            Filters = (1 << 5),
            Outputs = (1 << 6),
            SceneItems = (1 << 7),
            MediaInputs = (1 << 8),
            Vendors = (1 << 9),
            Ui = (1 << 10),
            All = (General | Config | Scenes | Inputs | Transitions | Filters | Outputs | SceneItems | MediaInputs | Vendors |
                   Ui),
            InputVolumeMeters = (1 << 16),
            InputActiveStateChanged = (1 << 17),
            InputShowStateChanged = (1 << 18),
            SceneItemTransformChanged = (1 << 19),
        }

        public static class Events
        {
            public abstract class Event
            {
                // unfortunate that this is the cleanest we can do
                internal abstract void Handle(Awaiter awaiter);
            };
            public class CurrentProgramSceneChanged : Event
            {
                public string sceneName;
                internal override void Handle(Awaiter awaiter)
                {
                    if (sceneName != null)
                    {
                        var m = Handler.r_sceneName.Match(sceneName);
                        if (!m.Success)
                            return;

                        awaiter.currentIndex = int.Parse(m.Groups[1].Value);
                        if (awaiter.activeNext)
                        {
                            awaiter.activeCurrent = true;
                            awaiter.CopyToNext(); // this ultimately sets activeNext 
                        }
                        else if (awaiter.activeCurrent) // if not activeNext but we were activeCurrent, we're fucked
                        {
                            awaiter.activeCurrent = false;
                            awaiter.activeSide = null;
                        }
                    }
                }

            }
            public class CurrentPreviewSceneChanged : Event
            {
                public string sceneName;
                internal override void Handle(Awaiter awaiter)
                {
                    if (sceneName != null)
                    {
                        var m = Handler.r_sceneName.Match(sceneName);
                        if (m.Success)
                            awaiter.nextIndex = int.Parse(m.Groups[1].Value);
                    }
                }
            }
            public class InputNameChanged : Event
            {
                public string inputName;
                internal override void Handle(Awaiter awaiter)
                {
                    if (awaiter.CheckActive(inputName) && awaiter.activeNext)
                        awaiter.Send(RequestsToPacket(awaiter.SetName()));
                }
            }
        }

        public static class Responses
        {
            public class Response
            {
                public class Status
                {
                    public bool result;
                    public int code;
                    public RequestStatus StatusCode => (RequestStatus)code;
                    public string comment;
                }

                internal Status status;
            }

            public class GetSceneList : Response
            {
                public string currentProgramSceneName;
                //public string currentProgramSceneUuid;
                public string currentPreviewSceneName;
                //public string currentPreviewSceneUuid;
            };

            public class GetInputList : Response
            {
                public class Input
                {
                    public string inputName;
                }
                public Input[] inputs;
            }

            public class GetInputSettings : Response
            {
                [AfterDecode]
                void Fix(ProxyObject raw) => inputSettings = (ProxyObject)raw["inputSettings"];

                internal ProxyObject inputSettings;
            }
        }

        public static class Requests
        {
            public class Request
            {
                internal virtual void Call(Response _) { }
            };

            public class Request<T> : Request where T : Response
            {
                internal Action<T> func;
                internal override void Call(Response r) => func?.Invoke((T)r);
            }

            [Serializable]
            public class RequestInfo
            {
                public string requestType;
                public Guid requestId = Guid.NewGuid();
                public Request requestData;

                public static RequestInfo Make<T>(T req) where T : Request => Make(typeof(T).Name, req);
                public static RequestInfo Make(string type, Request req)
                {

                    var ret = new RequestInfo
                    {
                        requestType = type,
                        requestData = req
                    };
                    Handler.requests.Add(ret.requestId, req);

                    return ret;
                }
            }

            [Serializable]
            public class MultiRequestInfo
            {
                public Guid requestId = Guid.NewGuid();
                public List<RequestInfo> requests = [];
            }

            public class GetSceneList : Request<Responses.GetSceneList>;
            public class GetInputList : Request<Responses.GetInputList>
            {
                public string inputKind = "color_source_v3";
            }

            public class GetInputSettings : Request<Responses.GetInputSettings>
            {
                public string inputName;
            }
            public class SetInputSettings : Request
            {
                public string inputName;
                public ProxyObject inputSettings;
            }
            public class SetInputName : Request
            {
                public string inputName;
                public string newInputName;
            }
        }


        [Serializable]
        public class OBSPacket(Opcodes opc, object data)
        {
            public OBSPacket() : this(0, null) { }
            public OBSPacket(string j) : this(0, null) => ParseJSON(j);


            internal Opcodes opcode = opc;
            [Include]
            public int op { get { return (int)opcode; } private set { opcode = (Opcodes)value; } }

            internal ProxyObject rawData;
            internal object data = data;
            [Include]
            public ProxyObject d { get { return (ProxyObject)JSON.Load(JSON.Dump(data, EncodeOptions.NoTypeHints)); } }

            public string ToJSON() => JSON.Dump(this, EncodeOptions.NoTypeHints);
            public void ParseJSON<T>(string json)
            {
                ParseJSON(json);
                JSON.MakeInto<T>(rawData, out var data);
                this.data = data;
            }
            public void ParseJSON(string json)
            {
                var v = JSON.Load(json);
                op = (int)v["op"];
                rawData = (ProxyObject)v["d"];
            }
        }

        internal static OBSPacket RequestToPacket<T>(T data) where T : Request => new(Opcodes.Request, RequestInfo.Make(data));

        internal static OBSPacket RequestsToPacket(params Request[] reqs)
        {
            if (reqs.Length == 1)
            {
                var req = reqs[0];
                return new(Opcodes.Request, RequestInfo.Make(req.GetType().Name, req));
            }

            var reqsData = new MultiRequestInfo();

            foreach (var req in reqs)
                reqsData.requests.Add(RequestInfo.Make(req.GetType().Name, req));

            return new(Opcodes.RequestBatch, reqsData);
        }

        static readonly MethodInfo makeInto = NeonLite.Helpers.Method(typeof(JSON), "MakeInto");
        internal static Event PacketToEvent(ProxyObject data)
        {
            var t = typeof(Events).GetNestedType(data["eventType"]);
            if (t == null)
                return null;

            if (data.Keys.Contains("eventData"))
            {
                object[] args = [data["eventData"], null];
                makeInto.MakeGenericMethod(t).Invoke(null, args);
                return (Event)args[1];
            }
            return (Event)Activator.CreateInstance(t);
        }
        internal static Response PacketToResponse(ProxyObject data)
        {
            Response ret;
            var t = typeof(Responses).GetNestedType(data["requestType"]);
            if (t != null)
            {
                if (data.Keys.Contains("responseData"))
                {
                    object[] args = [data["responseData"], null];
                    makeInto.MakeGenericMethod(t).Invoke(null, args);
                    ret = (Response)args[1];
                }
                else
                    ret = (Response)Activator.CreateInstance(t);
            }
            else ret = new();
            ret.status = data["requestStatus"].Make<Response.Status>();
            return ret;
        }
    }
}
