using DocXToPDFConverterContracts.Messages;
using DocXToPDFConverterContracts.Models;
using DocXToPDFConverterUtils;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SampleClient
{
    class Program
    {

        /*
         * Quando l'utente digita il numero di richieste da effettuare
         * il programma crea N copie di Progetto.docx nella subdir docx.
         * Il worker inserirà i pdf nella subdir pdf
         */

        private static readonly string docxTemplatePath = @"C:\Users\Demetrio\Desktop\Progetto.docx";
        private static readonly string pdfSubFolderName = "pdf";
        private static readonly string docxSubFolderName = "docx";

        static void Main(string[] args)
        {
            Console.WriteLine("Client Started");
            Console.Write("Connecting to service bus...");
            var factory = new ConnectionFactory() { HostName = "localhost", UserName = "guest", Password = "guest" };
            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                Console.WriteLine("done\n");

                while (true)
                {
                    Console.WriteLine("Enter number of files to process or just Enter to exit");
                    string cmd = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(cmd))
                        break;
                    int numOfFiles;
                    if (!int.TryParse(cmd, out numOfFiles))
                    {
                        Console.WriteLine("Invalid number!");
                        continue;
                    }
                    if (numOfFiles < 0 || numOfFiles > 100)
                    {
                        Console.WriteLine("Type any number between 1 and 100");
                        continue;
                    }

                    // Request composition
                    var timestamp = DateTime.Now.ToString("yyyyMMddTHHmmssFFF");
                    var requestList = GenerateListOfRequests(numOfFiles, timestamp);

                    foreach (var rl in requestList)
                    {
                        Console.WriteLine($"Requested file {rl.SourceFileFullPath}");
                    }

                    var request = new FileListConversionRequest
                    {
                        Files = requestList
                    };

                    var consumer = new EventingBasicConsumer(channel);

                    var manualResetEvent = new ManualResetEvent(false);
                    consumer.Received += (o, e) =>
                    {
                        var message = SerializeUtils.RabbitDeserialize<FileListConversionResponse>(e.Body);
                        Console.WriteLine("done");

                        foreach (var rp in message.Files)
                        {
                            Console.WriteLine($"SrcFile {rp.SourceFileFullPath}, out: {rp.ConvertedFileFullPath}");
                        }

                        manualResetEvent.Set();
                    };
                    var queue = channel.QueueDeclare().QueueName;
                    channel.BasicConsume(queue, true, "CLIENT", consumer);


                    var bp = channel.CreateBasicProperties();
                    bp.ReplyTo = queue;

                    channel.BasicPublish("", DocXToPDFConverterContracts.DocXToPDFConverterConstants.PDFCoordinatorInputQueueName,
                        bp, SerializeUtils.RabbitSerialize(request));

                    Console.WriteLine($"Request to process {numOfFiles} files (timestamp {timestamp}, sent to service bus.");
                    Console.Write("Waiting for response...");

                    manualResetEvent.WaitOne();
                    Console.Write("Disconnecting from service bus...");

                    break;
                }
            }

            Console.WriteLine("done");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }


        static IEnumerable<FileConversionRequestEntry> GenerateListOfRequests(int count, string timestamp)
        {
            var rnd = new Random();
            var numOf = count;
            var l = new List<FileConversionRequestEntry>();

            var rootPath = Path.GetDirectoryName(docxTemplatePath);
            var docxDir = Path.Combine(rootPath, docxSubFolderName);
            var pdfDir = Path.Combine(rootPath, pdfSubFolderName);

            Directory.CreateDirectory(docxDir);
            Directory.CreateDirectory(pdfDir);

            for (int i = 0; i < numOf; i++)
            {
                var rndString = $"{RandomString(7, rnd)}_{timestamp}";
                var sourceDox = Path.Combine(docxDir, rndString + ".docx");
                var targetPDFFile = Path.Combine(pdfDir, rndString + ".pdf");
                File.Copy(docxTemplatePath, sourceDox);

                l.Add(new FileConversionRequestEntry
                {
                    SourceFileFullPath = sourceDox,
                    ConvertedFileFullPath = targetPDFFile
                });
            }
            return l;
        }

        static string RandomString(int length, Random random)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}
