using BepInEx.Logging;
using R2API.Networking.Interfaces;
using RoR2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.Networking;

namespace TILER2 {
    public static class NetUtil {
        internal static void Setup() {
            R2API.Networking.NetworkingAPI.RegisterMessageType<MsgSendChatMsg>();
            R2API.Networking.NetworkingAPI.RegisterMessageType<MsgSendConMsg>();
        }
        #region Read/Write Helpers
        public static byte[] PackStringArray(string[] strings) {
            var header = new List<int>();
            header.Add(strings.Length);
            foreach(var str in strings) {
                header.Add(str.Length);
            }
            var serBytes = header.SelectMany(BitConverter.GetBytes)
                .Concat(System.Text.Encoding.Unicode.GetBytes(string.Join("", strings)))
                .ToArray();

            using(var inputStream = new MemoryStream(serBytes))
            using(var outputStream = new MemoryStream()) {
                using(var compressor = new System.IO.Compression.DeflateStream(outputStream, System.IO.Compression.CompressionMode.Compress)) {
                    inputStream.CopyTo(compressor);
                }

                return outputStream.ToArray();
            }
        }

        public static string[] UnpackStringArray(byte[] packed) {
            string[] strings;
            int[] stringLengths;
            using(var inputStream = new MemoryStream(packed))
            using(var outputStream = new MemoryStream()) {
                using(var compressor = new System.IO.Compression.DeflateStream(inputStream, System.IO.Compression.CompressionMode.Decompress)) {
                    compressor.CopyTo(outputStream);
                }

                outputStream.Seek(0, SeekOrigin.Begin);

                //read 1 int: # of entries
                byte[] outputBuffer = new byte[4];
                outputStream.Read(outputBuffer, 0, 4);
                stringLengths = new int[BitConverter.ToInt32(outputBuffer, 0)];
                strings = new string[stringLengths.Length];
                //read # entries ints: string bytecounts
                for(var i = 0; i < stringLengths.Length; i++) {
                    outputStream.Read(outputBuffer, 0, 4);
                    stringLengths[i] = BitConverter.ToInt32(outputBuffer, 0);
                }
                //read remaining: strings
                using(var reader = new StreamReader(outputStream, System.Text.Encoding.Unicode)) {
                    for(var i = 0; i < stringLengths.Length; i++) {
                        char[] readerBuffer = new char[stringLengths[i]];
                        reader.Read(readerBuffer, 0, stringLengths[i]);
                        strings[i] = new string(readerBuffer);
                    }
                }
            }

            return strings;
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
