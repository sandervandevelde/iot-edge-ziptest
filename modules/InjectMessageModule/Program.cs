namespace InjectMessageModule
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

    using Newtonsoft.Json;

    class Program
    {
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
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module 'iot-edge-injectmessage' client initialized.");

            await ioTHubModuleClient.SetMethodHandlerAsync("InjectMessage", MethodModule, ioTHubModuleClient);

            Console.WriteLine("Direct method handler 'InjectMessage' attached.");
            Console.WriteLine("Output handler 'output1' defined.");
        }

        private async static Task<MethodResponse> MethodModule(MethodRequest methodRequest, object userContext)
        {
            ModuleClient ioTHubModuleClient = (ModuleClient)userContext;

            var requestText = Encoding.UTF8.GetString(methodRequest.Data);

            Console.WriteLine($"Method request 'InjectMessage' received at '{DateTime.Now}' with message '{requestText}'");

            using (var outputMessage = new Message(methodRequest.Data))
            { 
                outputMessage.ContentEncoding = "utf-8";
                outputMessage.ContentType = "application/json";

                await ioTHubModuleClient.SendEventAsync("output1", outputMessage);

                System.Console.WriteLine("Message sent to output 'output1'");
            }

            var responseBody = new ResponseBody{result = "Message sent to output 'output1'"};
            var json = JsonConvert.SerializeObject(responseBody);
            var response = new MethodResponse(Encoding.UTF8.GetBytes(json), 200);

            return response;
        }
    }

    public class ResponseBody
    {
        public string result {get;set;}
    }
}
