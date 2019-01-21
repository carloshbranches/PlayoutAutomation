﻿//#undef DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Serialization;

namespace TAS.Remoting.Client
{
    public class RemoteClient: TcpConnection
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly JsonSerializer _serializer;
        private readonly ClientReferenceResolver _referenceResolver;
        private object _initialObject;
        private readonly Dictionary<Guid, SocketMessage> _receivedMessages = new Dictionary<Guid, SocketMessage>();
        private readonly AutoResetEvent _messageReceivedAutoResetEvent = new AutoResetEvent(false);


        private const int QueryTimeout =
#if DEBUG 
            50000
#else
            3000
#endif
            ;


        public RemoteClient(string address): base(address)
        {
            _serializer = JsonSerializer.CreateDefault();
            _serializer.Context = new StreamingContext(StreamingContextStates.Remoting, this);
            _serializer.PreserveReferencesHandling = PreserveReferencesHandling.Objects;
            _referenceResolver = new ClientReferenceResolver();
            _serializer.ReferenceResolver = _referenceResolver;
            _serializer.TypeNameHandling = TypeNameHandling.Objects | TypeNameHandling.Arrays;
#if DEBUG
            _serializer.Formatting = Formatting.Indented;
#endif      
            StartThreads();
        }


        protected override void OnDispose()
        {
            base.OnDispose();
            _referenceResolver.Dispose();
        }
        
        public ISerializationBinder Binder
        {
            get => _serializer.SerializationBinder;
            set => _serializer.SerializationBinder = value;
        }

        public T GetInitalObject<T>()
        {
            try
            {
                var queryMessage =
                    WebSocketMessageCreate(SocketMessage.SocketMessageType.RootQuery, null, null, 0);
                var response = SendAndGetResponse<T>(queryMessage, null);
                _initialObject = response;
                return response;
            }
            catch (Exception e)
            {
                Logger.Error(e, "From GetInitialObject:");
                throw;
            }
        }

        public T Query<T>(ProxyBase dto, string methodName, params object[] parameters)
        {
            try
            {
                var queryMessage = WebSocketMessageCreate(
                    SocketMessage.SocketMessageType.Query,
                    dto,
                    methodName,
                    parameters.Length);
                return SendAndGetResponse<T>(queryMessage, new SocketMessageArrayValue {Value = parameters});
            }
            catch (Exception e)
            {
                Logger.Error("From Query for {0}: {1}", dto, e);
                throw;
            }
        }

        public T Get<T>(ProxyBase dto, string propertyName)
        {
            try
            {
                var queryMessage = WebSocketMessageCreate(
                    SocketMessage.SocketMessageType.Get,
                    dto,
                    propertyName,
                    0
                );
                return SendAndGetResponse<T>(queryMessage, null);
            }
            catch (Exception e)
            {
                Logger.Error("From Get {0}: {1}", dto, e);
                throw;
            }
        }

        public void Invoke(ProxyBase dto, string methodName, params object[] parameters)
        {
            var queryMessage = WebSocketMessageCreate(
                SocketMessage.SocketMessageType.Invoke,
                dto,
                methodName,
                parameters.Length);
            using (var valueStream = Serialize(new SocketMessageArrayValue {Value = parameters}))
            {
                Send(queryMessage.ToByteArray(valueStream));
            }
        }

        public void Set(ProxyBase dto, object value, string propertyName)
        {
            var queryMessage = WebSocketMessageCreate(
                SocketMessage.SocketMessageType.Set,
                dto,
                propertyName,
                1);
            using (var valueStream = Serialize(value))
                Send(queryMessage.ToByteArray(valueStream));
        }

        public void EventAdd(ProxyBase dto, string eventName)
        {
            var queryMessage = WebSocketMessageCreate(
                SocketMessage.SocketMessageType.EventAdd,
                dto,
                eventName,
                0);
            Send(queryMessage.ToByteArray(null));
        }

        public void EventRemove(ProxyBase dto, string eventName)
        {
            var queryMessage = WebSocketMessageCreate(
                SocketMessage.SocketMessageType.EventRemove,
                dto,
                eventName,
                0);
            Send(queryMessage.ToByteArray(null));
        }

        private Stream Serialize(object o)
        {
            if (o == null)
                return null;
            var stream = new MemoryStream();
            using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
            {
                _serializer.Serialize(writer, o);
                return stream;
            }
        }

        internal T Deserialize<T>(SocketMessage message)
        {
            using (var valueStream = message.ValueStream)
            {
                if (valueStream == null)
                    return default(T);
                using (var reader = new StreamReader(valueStream))
                using (var jsonReader = new JsonTextReader(reader))
                    return _serializer.Deserialize<T>(jsonReader);
            }
        }

        protected override void OnMessage(byte[] data)
        {
            var message = new SocketMessage(data);
            if (message.MessageType != SocketMessage.SocketMessageType.RootQuery && _initialObject == null)
                return;
            switch (message.MessageType)
            {
                case SocketMessage.SocketMessageType.EventNotification:
                    _referenceResolver.ResolveReference(message.DtoGuid)?.OnEventNotificationMessage(message);
                    break;
                case SocketMessage.SocketMessageType.ObjectDisposed:
                    _referenceResolver.ResolveReference(message.DtoGuid)?.Dispose();
                    break;
                default:
                    lock (((IDictionary)_receivedMessages).SyncRoot)
                    {
                        _receivedMessages[message.MessageGuid] = message;
                        _messageReceivedAutoResetEvent.Set();
                    }
                    break;
            }
        }


        private SocketMessage WebSocketMessageCreate(SocketMessage.SocketMessageType socketMessageType, IDto dto, string memberName, int paramsCount)
        {
            return new SocketMessage
            {
                MessageType = socketMessageType,
                DtoGuid = dto?.DtoGuid ?? Guid.Empty,
                MemberName = memberName,
                ParametersCount = paramsCount
            };
        }

        private T SendAndGetResponse<T>(SocketMessage query, object value)
        {
            using (var valueStream = Serialize(value))
            {
                var valueBytes = query.ToByteArray(valueStream);
                Send(valueBytes);
            }
            while (IsConnected)
            {
                _messageReceivedAutoResetEvent.WaitOne(5);
                SocketMessage response;
                lock (((IDictionary) _receivedMessages).SyncRoot)
                {
                    if (_receivedMessages.TryGetValue(query.MessageGuid, out response))
                        _receivedMessages.Remove(query.MessageGuid);
                }
                if (response == null)
                    continue;
                if (response.MessageType == SocketMessage.SocketMessageType.Exception)
                    throw Deserialize<Exception>(response);
                return Deserialize<T>(response);
            }
            return default(T);
        }

    }
}
