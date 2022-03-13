using BepInEx.Logging;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine.Networking;

namespace TILER2 {
    public static class NetUtil {
        internal static void Setup() {
            R2API.Networking.NetworkingAPI.RegisterMessageType<MsgSendChatMsg>();
            R2API.Networking.NetworkingAPI.RegisterMessageType<MsgSendConMsg>();
        }
        #region Reader/Writer Extensions
        public static string[] ReadStringArray(this NetworkReader reader) {
            int len = reader.ReadInt32();
            string[] retv = new string[len];
            for(int i = 0; i < len; i++) {
                retv[i] = reader.ReadString();
            }
            return retv;
        }
        public static void WriteStringArray(this NetworkWriter writer, string[] value) {
            writer.Write(value.Length);
            for(int i = 0; i < value.Length; i++) {
                writer.Write(value[i]);
            }
        }
        #endregion
        #region Remote Console Messaging
        /// <summary>
        /// Send a console log message to the target NetworkUser, or the local client if there is no server active.
        /// </summary>
        /// <param name="user">The NetworkUser to send a console message to.</param>
        /// <param name="msg">The content of the sent message.</param>
        /// <param name="severity">The severity of the sent message.</param>
        public static void SendConMsg(NetworkUser user, string msg, LogLevel severity = LogLevel.Message) {
            if(!NetworkServer.active) {
                SendConMsgInternal(msg, severity);
            } else {
                ServerSendConMsg(user, msg, severity);
            }
        }
        private static void SendConMsgInternal(string msg, LogLevel severity) {
            switch(severity) {
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(msg);
                    break;
                case LogLevel.Error:
                    UnityEngine.Debug.LogError(msg);
                    break;
                default:
                    UnityEngine.Debug.Log(msg);
                    break;
            }
        }

        /// <summary>
        /// Send a console log message to the target NetworkUser.
        /// </summary>
        /// <param name="user">The NetworkUser to send a console message to.</param>
        /// <param name="msg">The content of the sent message.</param>
        /// <param name="severity">The severity of the sent message.</param>
        public static void ServerSendConMsg(NetworkUser user, string msg, LogLevel severity = LogLevel.Message) {
            if(!NetworkServer.active) {
                TILER2Plugin._logger.LogError("NetUtil.ServerSendConMsg called on client");
                return;
            }
            new MsgSendConMsg(msg, severity).Send(user.connectionToClient);
        }

        private struct MsgSendConMsg : INetMessage {
            private string _msg;
            private LogLevel _severity;

            public void Serialize(NetworkWriter writer) {
                writer.Write((int)_severity);
                writer.Write(_msg);
            }

            public void Deserialize(NetworkReader reader) {
                _severity = (LogLevel)reader.ReadInt32();
                _msg = reader.ReadString();
            }

            public void OnReceived() {
                SendConMsgInternal(_msg, _severity);
            }

            public MsgSendConMsg(string msg, LogLevel severity = LogLevel.Message) {
                _msg = msg;
                _severity = severity;
            }
        }
        #endregion
        #region Remote Chat Messaging
        /// <summary>
        /// Send a chat message to all players in a server.
        /// </summary>
        /// <param name="msg">The content of the sent message.</param>
        public static void ServerSendGlobalChatMsg(string msg) {
            if(!NetworkServer.active) {
                TILER2Plugin._logger.LogError("NetUtil.ServerSendGlobalChatMsg called on client");
                return;
            }
            new MsgSendChatMsg(msg).Send(R2API.Networking.NetworkDestination.Clients);
        }

        /// <summary>
        /// Send a chat message to the target NetworkUser.
        /// </summary>
        /// <param name="user">The NetworkUser to send a console message to.</param>
        /// <param name="msg">The content of the sent message.</param>
        public static void ServerSendChatMsg(NetworkUser user, string msg) {
            if(!NetworkServer.active) {
                TILER2Plugin._logger.LogError("NetUtil.ServerSendChatMsg called on client");
                return;
            }
            new MsgSendChatMsg(msg).Send(user.connectionToClient);
        }

        private struct MsgSendChatMsg : INetMessage {
            private string _msg;

            public void Serialize(NetworkWriter writer) {
                writer.Write(_msg);
            }

            public void Deserialize(NetworkReader reader) {
                _msg = reader.ReadString();
            }

            public void OnReceived() {
                Chat.AddMessage(_msg);
            }

            public MsgSendChatMsg(string msg) {
                _msg = msg;
            }
        }
        #endregion
    }
}
