using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Bot.Connector.DirectLine;
using Microsoft.PowerVirtualAgents.Samples.BotConnectorApp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace Glozic.Function
{
    public class SendMessage
    {
        private readonly ILogger _logger;
        private static string? _watermark = null;
        private static string? s_endConversationMessage = "bye";
        private static BotService? s_botService;
        protected static IDictionary<string, string> s_tokens = new Dictionary<string, string>();

        public SendMessage(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SendMessage>();
            s_botService = new BotService()
            {
                BotName = "Test Bot",
                BotId = "25b3e891-79b6-44b5-9a3f-84a298b25636",
                TenantId = "90d4d15c-f7ee-4f77-aaba-e70a2ef0caea",
                TokenEndPoint = "https://default90d4d15cf7ee4f77aabae70a2ef0ca.ea.environment.api.powerplatform.com/powervirtualagents/botsbyschema/cre16_testBot/directline/token?api-version=2022-03-01-preview"
            };
        }

        [Function("SendMessage")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] 
                HttpRequestData req, 
                FunctionContext context)
        {
            var bodyStream = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("body: " + bodyStream);
            dynamic jsonObj = JObject.Parse(bodyStream);
            Console.WriteLine("jsonObj: " + jsonObj);
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            if (s_botService is not null) {
                var token = await s_botService.GetTokenAsync();
            
                string from = $"whatsapp:{Convert.ToString(jsonObj.entry[0].changes[0].value.messages[0].from)}"?? "";
                string body = Convert.ToString(jsonObj.entry[0].changes[0].value.messages[0].text.body)?? "";
                string to = $"whatsapp:{Convert.ToString(jsonObj.entry[0].changes[0].value.metadata.display_phone_number)}"?? "";
                if (!s_tokens.ContainsKey(from)) {
                    s_tokens.Add(from, token);
                }

                _logger.LogInformation($"s_tokens: {s_tokens[from]}");
                _logger.LogInformation($"From,Body,To : {from} , {body} , {to}");
        
                _logger.LogInformation("C# HTTP trigger function processed a request on .NET7.0 Isolated.");
                string myToken = s_tokens[from];
                string responseStr = await StartConversation(from, body, to, myToken);
                
                var url = "https://graph.facebook.com/v17.0/108716175377344/messages";
                var client = new RestClient(url);
                var request = new RestRequest(url, Method.Post);
                //request.RequestFormat = DataFormat.Json;
                request.AddHeader("content-type", "application/json");
                request.AddHeader("Authorization", "Bearer EAAL8CAAFGXwBO2xGeLD8wxgJ1gZBkeaSypXnb42qBgmfVKKE2hmmywLwWk6vzSocfZCP0omQSatOktsr4wQ2oUaQOc1J0DPEMmwzyzfN3ZCfQvEEb6s1G12aQAZBRbEWi2KAb15iS6mWMscfhuVOJSrLybjeZCoXboyvnncahbmiZCNsD9jQZAH4noHUuyoEKiJ");
                var bodyObj = new {
                    body = responseStr
                };
                var jsonBody = new {
                    messaging_product = "whatsapp",
                    to = "6583327738", 
                    type = "text", 
                    text = bodyObj
                };
                request.AddJsonBody(jsonBody);
                RestResponse res = await client.ExecuteAsync(request);
                Console.WriteLine("res.Content: " + res.Content);
            }
            return response;
        }

        private async Task<string> StartConversation(string from, string inputMsg, string to, string token = "")
        {
            Console.WriteLine("token: " + token);
            using (var directLineClient = new DirectLineClient(token))
            {
                var conversation = await directLineClient.Conversations.StartConversationAsync();
                var conversationtId = conversation.ConversationId;
                Console.WriteLine(conversationtId + ": " + inputMsg);
                //while (!string.Equals(inputMessage = , s_endConversationMessage, StringComparison.OrdinalIgnoreCase))
                
                if (!string.IsNullOrEmpty(inputMsg) && !string.Equals(inputMsg, s_endConversationMessage))
                {
                    // Send user message using directlineClient
                    await directLineClient.Conversations.PostActivityAsync(conversationtId, new Activity()
                    {
                        Type = ActivityTypes.Message,
                        From = new ChannelAccount { Id = "userId", Name = "userName" },
                        Text = inputMsg,
                        TextFormat = "plain",
                        Locale = "en-Us",
                    });

                    // Get bot response using directlinClient
                    List<Activity> responses = await GetBotResponseActivitiesAsync(directLineClient, conversationtId);
                    //return BotReplyAsAPIResponse(responses);
                    return BotReplyAsAPIResponse(responses, from, to);
                }

                return ""; //"Thank you.";
            }
        }

        private static string BotReplyAsAPIResponse(List<Activity> responses, string from, string to)
        {
            string responseStr = "";
            string responseMediaUrl = "";
            string responseAct = "";
            responses?.ForEach(responseActivity =>
            {
                // responseActivity is standard Microsoft.Bot.Connector.DirectLine.Activity
                // See https://github.com/Microsoft/botframework-sdk/blob/master/specs/botframework-activity/botframework-activity.md for reference
                // Showing examples of Text & SuggestedActions in response payload
                Console.WriteLine(responseActivity.Text);
                if (!string.IsNullOrEmpty(responseActivity.Text))
                {
                    responseStr = responseStr + string.Join(Environment.NewLine, responseActivity.Text);
                }

                if (responseActivity.Attachments != null)
                {
                    foreach (Attachment attachment in responseActivity.Attachments)
                    {
                        Console.WriteLine("attachment.ContentType: " + attachment.ContentType);
                        var jsonStr = JsonConvert.SerializeObject(attachment.Content, Formatting.None);
                        Console.WriteLine(jsonStr);
                        dynamic jsonObj = JObject.Parse(jsonStr);
                        Console.WriteLine(jsonObj);
                        switch (attachment.ContentType)
                        {
                            case "application/vnd.microsoft.card.hero":
                                Console.WriteLine(jsonObj.images[0].url);
                                responseMediaUrl = jsonObj.images[0].url;
                                break;

                            case "image/png":
                                Console.WriteLine($"Opening the requested image '{attachment.ContentUrl}'");
                                break;

                            case "application/vnd.microsoft.card.video":
                                Console.WriteLine(jsonObj.media[0].url);
                                responseMediaUrl = jsonObj.media[0].url;
                                break;
                        }
                    }
                }

                if (responseActivity.SuggestedActions != null && responseActivity.SuggestedActions.Actions != null)
                {
                    responseAct = responseActivity.SuggestedActions.Actions[0].Title;
                    Console.WriteLine("Actions: " + responseAct);
                    var options = responseActivity.SuggestedActions?.Actions?.Select(a => a.Title).ToList()?? new List<string>(0);
                    responseStr = responseStr + $"\t{string.Join(" | ", options)}";
                }
            });

            Console.WriteLine("responseStr=> " + responseStr);
            /*
            TwilioClient.Init(accountSid, authToken);
            if (responseMediaUrl != "") {
                var mediaUrl = new[] {
                new Uri(responseMediaUrl)
                }.ToList();

                var message = MessageResource.Create(
                    body: responseStr,
                    mediaUrl: mediaUrl,
                    from: new Twilio.Types.PhoneNumber(to),
                    to: new Twilio.Types.PhoneNumber(from)
                );
                return "";
            } else if (responseAct != "") {
                var message = MessageResource.Create(
                    from: new Twilio.Types.PhoneNumber(to),
                    body: "Can I go ahead and ask you some questions?",
                    to: new Twilio.Types.PhoneNumber(from)
                );
                return "";
            } /*else {
                var message = MessageResource.Create(
                    body: responseStr,
                    from: new Twilio.Types.PhoneNumber("whatsapp:+14155238886"),
                    to: new Twilio.Types.PhoneNumber("whatsapp:+6583327738")
                );
            }*/

            return responseStr;
        }

        /// <summary>
        /// Use directlineClient to get bot response
        /// </summary>
        /// <returns>List of DirectLine activities</returns>
        /// <param name="directLineClient">directline client</param>
        /// <param name="conversationtId">current conversation ID</param>
        /// <param name="botName">name of bot to connect to</param>
        private static async Task<List<Activity>> GetBotResponseActivitiesAsync(DirectLineClient directLineClient, string conversationtId)
        {
            ActivitySet? response = null;
            List<Activity>? result = new List<Activity>();

            do
            {
                response = await directLineClient.Conversations.GetActivitiesAsync(conversationtId, _watermark);
                if (response == null)
                {
                    // response can be null if directLineClient token expires
                    Console.WriteLine("Conversation expired. Press any key to exit.");
                    Console.Read();
                    directLineClient.Dispose();
                    Environment.Exit(0);
                }

                _watermark = response?.Watermark;
                result = response?.Activities?.Where(x =>
                    x.Type == ActivityTypes.Message &&
                    string.Equals(x.From.Name, s_botService.BotName, StringComparison.Ordinal)).ToList();

                if (result != null && result.Any())
                {
                    return result;
                }

                Thread.Sleep(1000);
            } while (response != null && response.Activities.Any());

            return new List<Activity>();
        }
    }
}
