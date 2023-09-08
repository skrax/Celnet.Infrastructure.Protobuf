using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Celnet.Domain;
using Celnet.Domain.Events;
using Celnet.Domain.Interfaces;
using Celnet.Infrastructure.Protobuf.Domain;
using FluentValidation;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Method = Celnet.Infrastructure.Protobuf.Domain.Method;

namespace Celnet.Infrastructure.Protobuf
{
    public class ProtobufPeerTransport
    {
        private readonly IApi<IMessage, IMessage>? _api;
        private readonly IPeer _peer;
        private readonly TypeRegistry _typeRegistry;

        private readonly IDictionary<string, TaskCompletionSource<IMessage>> _responseAwaiter;

        private readonly RequestValidator _requestValidator = new RequestValidator();
        private readonly ResponseValidator _responseValidator = new ResponseValidator();

        public ProtobufPeerTransport(IApi<IMessage, IMessage> api, IPeer peer, TypeRegistry typeRegistry,
            IDictionary<string, TaskCompletionSource<IMessage>> responseAwaiter)
        {
            _api = api;
            _typeRegistry = typeRegistry;
            _responseAwaiter = responseAwaiter;
            _peer = peer;
            _peer.OnPeerReceive += OnReceive;
        }

        public ProtobufPeerTransport(IPeer peer, TypeRegistry typeRegistry,
            IDictionary<string, TaskCompletionSource<IMessage>> responseAwaiter)
        {
            _typeRegistry = typeRegistry;
            _responseAwaiter = responseAwaiter;
            _peer = peer;
            _peer.OnPeerReceive += OnReceive;
        }

        public static ProtobufPeerTransport AsServer(IApi<IMessage, IMessage> api, IPeer peer,
            TypeRegistry typeRegistry, bool isConcurrentContext)
        {
            var dict = isConcurrentContext
                ? new ConcurrentDictionary<string, TaskCompletionSource<IMessage>>()
                    as IDictionary<string, TaskCompletionSource<IMessage>>
                : new Dictionary<string, TaskCompletionSource<IMessage>>();
            
            return new ProtobufPeerTransport(api, peer, typeRegistry, dict);
        }

        public static ProtobufPeerTransport AsClient(IPeer peer, TypeRegistry typeRegistry, bool isConcurrentContext)
        {
             var dict = isConcurrentContext
                            ? new ConcurrentDictionary<string, TaskCompletionSource<IMessage>>()
                                as IDictionary<string, TaskCompletionSource<IMessage>>
                            : new Dictionary<string, TaskCompletionSource<IMessage>>();
                        
            return new ProtobufPeerTransport(peer, typeRegistry, dict);
        }

        private void OnReceive(PeerReceiveEvent receiveEvent)
        {
            switch (receiveEvent.ChannelId)
            {
                case ProtobufConfig.RequestChannelId:
                    HandleRequest(receiveEvent.PeerId, Request.Parser.ParseFrom(receiveEvent.Data));
                    break;
                case ProtobufConfig.ResponseChannelId:
                    HandleResponse(Response.Parser.ParseFrom(receiveEvent.Data));
                    Response.Parser.ParseFrom(receiveEvent.Data);
                    break;
                case ProtobufConfig.EventChannelId:
                    HandleEvent(Request.Parser.ParseFrom(receiveEvent.Data));
                    break;
                default: throw new NotImplementedException();
            }
        }

        private void HandleEvent(Request request)
        {
            _requestValidator.ValidateAndThrow(request);

            _api!.Event(request.Route, request.Body.Unpack(_typeRegistry));
        }

        private void HandleRequest(uint peerId, Request request)
        {
            _requestValidator.ValidateAndThrow(request);

            var responseBody = request.Method switch
            {
                Method.Get => _api!.Get(request.Route, request.Body?.Unpack(_typeRegistry)),
                Method.Post => _api!.Post(request.Route, request.Body.Unpack(_typeRegistry)),
                Method.Put => _api!.Put(request.Route, request.Body.Unpack(_typeRegistry)),
                Method.Delete => _api!.Delete(request.Route, request.Body.Unpack(_typeRegistry)),
                _ => throw new ArgumentOutOfRangeException()
            };

            var response = new Response
            {
                Id = Guid.NewGuid().ToString(),
                RequestId = request.Id,
                Route = request.Route,
                Body = Any.Pack(responseBody),
            };
            _responseValidator.ValidateAndThrow(response);
            if (!_peer.TrySend(new PeerSendArgs
                {
                    PeerId = peerId,
                    ChannelId = ProtobufConfig.ResponseChannelId,
                    Data = response.ToByteArray()
                })) throw new InvalidOperationException();
        }

        private void HandleResponse(Response response)
        {
            _responseValidator.ValidateAndThrow(response);

            if (_responseAwaiter.Remove(response.RequestId, out var tsc))
            {
                tsc.SetResult(response.Body.Unpack(_typeRegistry));
            }
        }

        public async Task<IMessage> GetAsync(string route, IMessage? body, uint peerId = 0) =>
            await CreateAndSendRequestAsync(route, Method.Get, body, peerId);

        public async Task<IMessage> PostAsync(string route, IMessage body, uint peerId = 0) =>
            await CreateAndSendRequestAsync(route, Method.Post, body, peerId);

        public async Task<IMessage> PutAsync(string route, IMessage body, uint peerId = 0) =>
            await CreateAndSendRequestAsync(route, Method.Put, body, peerId);

        public async Task<IMessage> DeleteAsync(string route, IMessage body, uint peerId = 0) =>
            await CreateAndSendRequestAsync(route, Method.Delete, body, peerId);

        public void PublishEvent(string route, IMessage body, uint peerId = 0)
        {
            var request = CreateRequest(route, Method.Event, body);

            if (!_peer.TrySend(new PeerSendArgs
                {
                    PeerId = peerId,
                    ChannelId = ProtobufConfig.EventChannelId,
                    Data = request.ToByteArray()
                })) throw new InvalidOperationException();
        }

        private Task<IMessage> CreateAndSendRequestAsync(string route, Method method, IMessage? body,
            uint peerId)
        {
            var request = CreateRequest(route, method, body);

            var tsc = CreateAwaiter(request);

            if (!_peer.TrySend(new PeerSendArgs
                {
                    PeerId = peerId,
                    ChannelId = ProtobufConfig.RequestChannelId,
                    Data = request.ToByteArray()
                })) throw new InvalidOperationException();

            return tsc.Task;
        }

        private Request CreateRequest(string route, Method method, IMessage? body)
        {
            var request = new Request
            {
                Id = Guid.NewGuid().ToString(),
                Method = method,
                Route = route
            };

            if (body != null) request.Body = Any.Pack(body);

            _requestValidator.ValidateAndThrow(request);

            return request;
        }

        private TaskCompletionSource<IMessage> CreateAwaiter(Request request)
        {
            var tsc = new TaskCompletionSource<IMessage>();
            _responseAwaiter.Add(request.Id, tsc);
            return tsc;
        }
    }
}