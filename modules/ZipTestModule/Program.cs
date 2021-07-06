namespace ZipTestModule
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;
    using ZipHelperLib;

    class Program
    {
        static int counter;

        private static bool DefaultUseGZip = true;
        private static bool UseGZip {get; set; } = DefaultUseGZip;

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);

            // Execute callback method for Twin desired properties updates
            var twin = await ioTHubModuleClient.GetTwinAsync();
            await onDesiredPropertiesUpdate(twin.Properties.Desired, ioTHubModuleClient);

            Console.WriteLine($"Desired properties supported: useGZip ({UseGZip})."); 

            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client 'iot-edge-ziptest' initialized.");

            // Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);

            Console.WriteLine("Input handler 'input1' attached.");
            Console.WriteLine("Output handler 'output1' defined.");
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);

            var moduleClient = userContext as ModuleClient;
            if (moduleClient == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);

            if (!string.IsNullOrEmpty(messageString))
            {
                var zippedMessageBytes = UseGZip 
                                            ? GZipHelper.Zip(messageBytes)
                                            : DeflateHelper.Zip(messageBytes);                

                Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}], length {messageBytes.Length} bytes zipped to {zippedMessageBytes.Length} bytes");

                using (var pipeMessage = new Message(zippedMessageBytes))
                {
                    if (UseGZip)
                    {
                        pipeMessage.ContentEncoding = "gzip";
                    }
                    else
                    {
                        pipeMessage.ContentEncoding = "deflate";
                    }

                    pipeMessage.ContentType = "application/zip";


                    foreach (var prop in message.Properties)
                    {
                        pipeMessage.Properties.Add(prop.Key, prop.Value);
                    }

                    await moduleClient.SendEventAsync("output1", pipeMessage);
                
                    var compressionType = UseGZip? "GZip" : "deflate";

                    Console.WriteLine($"Received message sent using {compressionType} compression type.");
                }
            }
            return MessageResponse.Completed;
        }

        private static async Task onDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext)
        {
            if (desiredProperties.Count == 0)
            {
                Console.WriteLine("Empty desired properties ignored.");

                return;
            }

            try
            {
                Console.WriteLine("Desired property change:");
                Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));

                var client = userContext as ModuleClient;

                if (client == null)
                {
                    throw new InvalidOperationException($"UserContext doesn't contain expected ModuleClient");
                }

                var reportedProperties = new TwinCollection();

                if (desiredProperties.Contains("useGZip"))
                {
                    if (desiredProperties["useGZip"] != null)
                    {
                        UseGZip = Convert.ToBoolean(desiredProperties["useGZip"]);
                    }
                    else
                    {
                        UseGZip = DefaultUseGZip;
                    }

                    Console.WriteLine($"UseGZip changed to {UseGZip}");

                    reportedProperties["useGZip"] = UseGZip;
                }
                else
                {
                    Console.WriteLine($"UseGZip ignored");
                }


                if (reportedProperties.Count > 0)
                {
                    await client.UpdateReportedPropertiesAsync(reportedProperties);
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine($"Desired properties change error: {ex.Message}");

                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine($"Error when receiving desired properties: {exception}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error when receiving desired properties: {ex.Message}");
            }
        }
    }
}
