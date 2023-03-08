using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using OpenAI.GPT3;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.Managers;
using OpenAI.GPT3.ObjectModels.RequestModels;
using OpenAI.GPT3.ObjectModels;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using SlackNet;
using SlackNet.SocketMode;
using SlackNet.Events;
using SlackNet.WebApi;
using SlackNet.Interaction.Experimental;
using Newtonsoft.Json.Linq;

//https://github.com/soxtoby/SlackNet/blob/master/Examples/NoContainerExample/Program.cs
//https://api.slack.com/apis/connections/socket
namespace ChatGPTSlackBot
{
    class AppSettings
    {
        public string SlackAppId { get; init; } = string.Empty;
        public string SlackApiToken { get; init; } = string.Empty;
        public string SlackAppLevelToken { get; init; } = string.Empty;
        public string OpenAIApiKey { get; init; } = string.Empty;
    }

    public class SlackBot : IEventHandler<MessageEvent>
    {
        private readonly ISlackApiClient _slack;
        private readonly IOpenAIService _openAISerivce;
        private readonly string _appId;
        public SlackBot(ISlackApiClient slack, IOpenAIService openAISerivce, string appId)
        {
            _slack = slack;
            _openAISerivce = openAISerivce;
            _appId = appId;
        }

        public async Task Handle(MessageEvent slackEvent)
        {
            if (!string.IsNullOrEmpty(slackEvent.Text.Trim()) == true)
            {
                var self = false;

                if (slackEvent.ExtraProperties != null)
                {
                    if (slackEvent.ExtraProperties.Keys.Contains("bot_id"))
                    {
                        self = true;
                    }
                    else if (slackEvent.ExtraProperties.Keys.Contains("bot_profile"))
                    {
                        var profile = (JObject)slackEvent.ExtraProperties["bot_profile"];
                        if (profile != null)
                        {
                            if (profile.ContainsKey("app_id"))
                            {
                                var appId = profile["app_id"]?.ToString();
                                if (appId.Equals(_appId))
                                {
                                    self = true;
                                }
                            }
                        }
                    }
                }
                if (!self)
                {
                    var completionResult = await _openAISerivce.Completions.CreateCompletion(new CompletionCreateRequest()
                    {
                        Prompt = slackEvent.Text.Trim(),
                        Model = Models.TextDavinciV2,
                        Temperature = 0.5F,
                        MaxTokens = 100,
                        N = 1
                    });

                    if (completionResult.Successful)
                    {
                        var response = completionResult.Choices[0].Text;
                        await _slack.Chat.PostMessage(new Message
                        {
                            Text = response,
                            Channel = slackEvent.Channel
                        });
                    }
                    else
                    {
                        if (completionResult.Error == null)
                        {
                            throw new Exception("Unknown Error");
                        }
                        Console.WriteLine($"{completionResult.Error.Code}: {completionResult.Error.Message}");
                    }
                }
            }
        }        
    }
    class Program
    {
        static async Task Main(string[] args)
        {

            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(AppDomain.CurrentDomain.BaseDirectory + "\\appsettings.json", optional: true, reloadOnChange: true)
                .Build()
                .Get<AppSettings>();
            

            var openAIService = new OpenAIService(new OpenAiOptions()
            {
                ApiKey = config.OpenAIApiKey
            });


            var slackServices = new SlackServiceBuilder()                
                .UseApiToken(config.SlackApiToken) // This gets used by the API client
                .UseAppLevelToken(config.SlackAppLevelToken) // This gets used by the socket mode client                
                .RegisterEventHandler<MessageEvent>(ctx => new SlackBot(ctx.ServiceProvider.GetApiClient(), openAIService, config.SlackAppId)); // Register your Slack handlers here

            Console.WriteLine("Connecting...");

            var client = slackServices.GetSocketModeClient();
            await client.Connect();

            Console.WriteLine("Connected. Press any key to exit...");
            await Task.Run(Console.ReadKey);      
            client.Disconnect();
        }        
    }
}