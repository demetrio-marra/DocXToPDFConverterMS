using DocXToPDFConverterContracts;
using DocXToPDFConverterContracts.Messages;
using DocXToPDFConverterUtils;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DocXToPDFConverterCoordinator
{
    class Program
    {
        private static readonly string CONSUMER_TAG = "DocXToPDFConverterCoordinator";

        static readonly ConcurrentDictionary<Guid, CountdownEvent> requestsList = new ConcurrentDictionary<Guid, CountdownEvent>();
        static readonly ConcurrentDictionary<Guid, IEnumerable<FileConversionResponse>> requestsResponse = new ConcurrentDictionary<Guid, IEnumerable<FileConversionResponse>>();

        static IModel globalChannel;
        static string callbackQueueName;

        static void Main(string[] args)
        {
            var factory = new ConnectionFactory() { HostName = "localhost", UserName = "guest", Password = "guest" };
            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                globalChannel = channel;
                InitSendQueue(channel);
                callbackQueueName = InitCallbackQueue(channel);

                var resultsCollectorConsumer = new EventingBasicConsumer(channel);
                resultsCollectorConsumer.Received += Consumer_Received;
                channel.BasicConsume(callbackQueueName,
                   true, CONSUMER_TAG, resultsCollectorConsumer);

                var incomingRequestsConsumer = new EventingBasicConsumer(channel);
                incomingRequestsConsumer.Received += IncomingRequestConsumer_Received;
                channel.BasicConsume(DocXToPDFConverterConstants.PDFCoordinatorInputQueueName,
                   false, CONSUMER_TAG + "_INCOMING_REQUESTS", incomingRequestsConsumer);

                Console.WriteLine("Press any key to terminate...");
                Console.ReadKey();
            }
        }

        static void InitSendQueue(IModel channel)
        {
            channel.QueueDeclare(DocXToPDFConverterConstants.PDFWorkerInputQueueName,
                true, false, false);
        }

        static string InitCallbackQueue(IModel channel)
        {
            return channel.QueueDeclare().QueueName;
        }

        private static void IncomingRequestConsumer_Received(object sender, BasicDeliverEventArgs e)
        {
            var message = SerializeUtils.RabbitDeserialize<FileListConversionRequest>(e.Body);

            Task.Run(() =>
            {
                var newGuid = Guid.NewGuid();
                var filesCount = message.Files.Count();
                var cde = new CountdownEvent(filesCount);
                requestsList.GetOrAdd(newGuid, cde);
                Console.WriteLine($"A new request to convert {filesCount} files acquired with guid {newGuid}.");
                Task.Run(() =>
                {
                    SendRequestToWorkers(newGuid, message.Files.Select(f => new FileConversionRequest
                    {
                        ConvertedFileFullPath = f.ConvertedFileFullPath,
                        SourceFileFullPath = f.SourceFileFullPath
                    }));
                });

                cde.Wait();
                Console.WriteLine($"All requests completed for guid {newGuid}");

                // collect results
                IEnumerable<FileConversionResponse> fileConversionResponses;
                if (requestsResponse.TryGetValue(newGuid, out fileConversionResponses))
                {
                    var channel = globalChannel;
                    var bp = channel.CreateBasicProperties();

                    var pload = new FileListConversionResponse
                    {
                        Files = fileConversionResponses.Select(ff => new DocXToPDFConverterContracts.Models.FileConversionResponseEntry
                        {
                            ConvertedFileFullPath = ff.ConvertedFileFullPath,
                            ErrorDescription = ff.ErrorDescription,
                            IsError = ff.IsError,
                            SourceFileFullPath = ff.SourceFileFullPath
                        })
                    };

                    channel.BasicPublish(exchange: "", routingKey: e.BasicProperties.ReplyTo,
                        basicProperties: bp, body: SerializeUtils.RabbitSerialize(pload));
                    channel.BasicAck(deliveryTag: e.DeliveryTag,
                          multiple: false);
                }

                requestsResponse.TryRemove(newGuid, out _);
                requestsList.TryRemove(newGuid, out _);
            });
        }

        private static void SendRequestToWorkers(Guid generatedUid,
            IEnumerable<FileConversionRequest> fileConversionRequests)
        {
            var channel = globalChannel;

            // send the request
            var props = channel.CreateBasicProperties();
            props.CorrelationId = generatedUid.ToString();
            props.ReplyTo = callbackQueueName;

            foreach (var request in fileConversionRequests)
            {
                channel.BasicPublish("", DocXToPDFConverterConstants.PDFWorkerInputQueueName,
                     props, SerializeUtils.RabbitSerialize(request));
            }
            Console.WriteLine($"Request Id {generatedUid} sent");
        }

        private static void Consumer_Received(object sender, BasicDeliverEventArgs e)
        {
            var message = SerializeUtils.RabbitDeserialize<FileConversionResponse>(e.Body);
            var cid = e.BasicProperties.CorrelationId;
            var guid = Guid.Parse(cid);

            var l = requestsResponse.AddOrUpdate(guid,
                en => new List<FileConversionResponse> {
                   message
                }
            , (en, lx) => lx.Concat(Enumerable.Repeat(message, 1)));
            Console.WriteLine($"Received response for request {cid} and file {message.ConvertedFileFullPath}");

            CountdownEvent cde;
            if (requestsList.TryGetValue(guid, out cde))
                cde.Signal();
        }
    }
}
