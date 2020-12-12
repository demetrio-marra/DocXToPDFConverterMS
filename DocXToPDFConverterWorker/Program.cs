using DocXToPDFConverterContracts;
using DocXToPDFConverterContracts.Messages;
using DocXToPDFConverterContracts.Models;
using DocXToPDFConverterUtils;
using GroupDocs.Conversion;
using GroupDocs.Conversion.Options.Convert;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DocXToPDFConverterWorker
{
    class Program
    {
        static IModel channel;
        static bool firstConversion = true;

        static void Main(string[] args)
        {
            var factory = new ConnectionFactory() { HostName = "localhost", UserName = "guest", Password = "guest" };
            using (var connection = factory.CreateConnection())
            using (channel = connection.CreateModel())
            {
                InitQueue(channel);
                InitConsumer(channel);

                Console.WriteLine("Press any key to quit");
                Console.ReadKey();
                channel.Close();
                channel.Dispose();
            }
        }

        static void InitQueue(IModel channel)
        {
            channel.QueueDeclare(DocXToPDFConverterConstants.PDFWorkerInputQueueName,
                true, false, false);

            // settiamo il prefetch soltanto per 
            // rendere più evidente che all'aumentare dei
            // worker aumenta la scalabilità del sistema
            channel.BasicQos(0, 1, false); // prefetch=1
        }

        static void InitConsumer(IModel channel)
        {
            var consumer = new EventingBasicConsumer(channel);
            channel.BasicConsume(DocXToPDFConverterConstants.PDFWorkerInputQueueName,
               true, "DocXToPDFConverterWorker", consumer);
            consumer.Received += Consumer_Received;
        }

        private static void Consumer_Received(object sender, BasicDeliverEventArgs ea)
        {
            if (firstConversion)
            {
                Console.WriteLine("First conversion received. Initialization time could be long");
                firstConversion = false;
            }

            var qpc = new Stopwatch();
            qpc.Start();
            var props = ea.BasicProperties;

            var replyProps = channel.CreateBasicProperties();
            replyProps.CorrelationId = props.CorrelationId;

            FileConversionResponseEntry response = null;
            var request = SerializeUtils.RabbitDeserialize<FileConversionRequest>(ea.Body);

            try
            {
                //SimulateVariableDelay(150, 2500);
                using (Converter converter = new Converter(request.SourceFileFullPath))
                {
                    PdfConvertOptions options = new PdfConvertOptions();
                    converter.Convert(request.ConvertedFileFullPath, options);
                }

                response = new FileConversionResponseEntry
                {
                    ConvertedFileFullPath = request.ConvertedFileFullPath,
                    SourceFileFullPath = request.SourceFileFullPath
                };
            }
            catch (Exception e)
            {
                response = new FileConversionResponseEntry
                {
                    ConvertedFileFullPath = request.ConvertedFileFullPath,
                    SourceFileFullPath = request.SourceFileFullPath,
                    IsError = true,
                    ErrorDescription = e.Message
                };
            }
            finally
            {
                var responseBytes = SerializeUtils.RabbitSerialize(response);
                channel.BasicPublish(exchange: "", routingKey: props.ReplyTo,
                  basicProperties: replyProps, body: responseBytes);
                qpc.Stop();
                Console.WriteLine($"Returned response for request {props.CorrelationId} and queue {props.ReplyTo} and file {response.ConvertedFileFullPath} in {qpc.ElapsedMilliseconds} ms");
            }
        }

        static void SimulateVariableDelay(int min, int max)
        {
            var rnd = new Random();
            var delay = rnd.Next(min, max);
            Task.Delay(delay).Wait();
        }
    }
}
