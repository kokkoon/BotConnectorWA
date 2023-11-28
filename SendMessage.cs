using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Bot.Connector.DirectLine;
using Microsoft.PowerVirtualAgents.Samples.BotConnectorApp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

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
            string? BotName = Environment.GetEnvironmentVariable("BotName");
            string? BotId = Environment.GetEnvironmentVariable("BotId");
            string? TenantId = Environment.GetEnvironmentVariable("TenantId");
            string? TokenEndPoint = Environment.GetEnvironmentVariable("TokenEndPoint");
            _logger.LogInformation("BotName, BotId: " + BotName + ", " + BotId);
            s_botService = new BotService()
            {
                BotName = BotName,
                BotId = BotId,
                TenantId = TenantId,
                TokenEndPoint = TokenEndPoint
            };
        }

        [Function("SendMessage")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] 
                HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request on .NET7.0 Isolated.");

            try
            {
                var challenge = System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("hub.challenge");
                _logger.LogInformation("challenge: " + challenge);
                var bodyStream = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("body: " + bodyStream);
                dynamic jsonObj = (bodyStream != "")? JObject.Parse(bodyStream): new JObject();
                Console.WriteLine("jsonObj: " + jsonObj);
                if (s_botService is not null && jsonObj.entry is not null && jsonObj.entry[0] is not null && jsonObj.entry[0].changes[0].value.messages is not null) {
                    var token = await s_botService.GetTokenAsync();
                
                    string from = Convert.ToString(jsonObj.entry[0].changes[0].value.messages[0].from)?? "";
                    string body = Convert.ToString(jsonObj.entry[0].changes[0].value.messages[0].text.body)?? "";
                    string to = Convert.ToString(jsonObj.entry[0].changes[0].value.metadata.display_phone_number)?? "";
                    if (!s_tokens.ContainsKey(from)) {
                        s_tokens.Add(from, token);
                    }

                    _logger.LogInformation($"s_tokens: {s_tokens[from]}");
                    _logger.LogInformation($"From,Body,To : {from} , {body} , {to}");
                    string myToken = s_tokens[from];
                    dynamic responseObj = await StartConversation(body, myToken);
                    Console.WriteLine("responseObj.Message: " + responseObj.Message);
                    Console.WriteLine("responseObj.Actions: " + responseObj.Actions);

                    var url = "https://graph.facebook.com/v17.0/174350675760672/messages";
                    var client = new RestClient(url);
                    var request = new RestRequest(url, Method.Post);
                    request.AddHeader("Content-Type", "application/json");
                    request.AddHeader("Authorization", "Bearer EAAT3N7NXVJoBOyjo3E2EZCmCGZBP55vCGoUTl5iC8jKGJ6abtfI06pOCLZA2MxLZAR5aYQPvKZAb1ed1YlGvpoZBpbELgy25isdQ0gdJZATjOPMZAlNPcWPQ9cspnZAJxS77V9ygHvSCY7ZBtYXhmklHGj9yXQK7h1Ju10M4uSm1H7w5M7eVCWq0YfIEgkngHP6G5y");
                    string payload = "";
                    if (responseObj.MediaUrl != "") {
                        payload = $@"{{
                            ""messaging_product"": ""whatsapp"",
                            ""to"": ""{from}"",
                            ""type"": ""{responseObj.MediaType}"",
                            ""{responseObj.MediaType}"": {{
                                ""link"": ""{responseObj.MediaUrl}""
                            }},
                            ""text"": {{
                                ""body"": ""{responseObj.Message}""
                            }}
                        }}";
                    } else if (responseObj.Actions != "") {
                        string resActions = responseObj.Actions;
                        string[] actions = resActions.Split("|");
                        StringBuilder builder = new StringBuilder(responseObj.Message);
                        builder.Replace("\"", "\'");
                        payload = $@"{{
                            ""messaging_product"": ""whatsapp"",
                            ""to"": ""{from}"",
                            ""type"": ""interactive"",
                            ""interactive"": {{
                                ""type"": ""button"",
                                ""body"": {{
                                    ""text"": ""{builder.ToString()}""
                                }},
                                ""action"": {{
                                    ""buttons"": [";
                        foreach (var (action, i)  in actions.Select((v,i) => (v, i)))
                        {
                            payload += $@"
                                        {{
                                            ""type"": ""reply"",
                                            ""reply"": {{
                                                    ""id"": ""UNIQUE_BUTTON_ID_{i}"",
                                                    ""title"": ""{action.Substring(0,Math.Min(action.Length, 20)).Trim()}""
                                                }}
                                        }},";
                        }
                        payload = payload.Remove(payload.Length - 1, 1);
                        payload += $@"
                                    ]
                                }}
                            }}
                        }}";
                    } else {
                        StringBuilder builder = new StringBuilder(responseObj.Message);
                        builder.Replace("\"", "\'");
                        payload = $@"{{
                            ""messaging_product"": ""whatsapp"",
                            ""to"": ""{from}"",
                            ""type"": ""text"",
                            ""text"": {{
                                ""body"": ""{builder.ToString()}""
                            }}
                        }}";
                    }
                    Console.WriteLine("payload: " + payload);
                    request.AddJsonBody(payload);
                    RestResponse res = await client.ExecuteAsync(request);
                    Console.WriteLine("res.Content: " + res.Content);
                }

                
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
                if (req.Method == "GET" && challenge is not null) response.WriteString(challenge);
                return response;
            } 
            catch (Exception ex)
            {
                _logger.LogInformation("Exception ... " + ex);

                var response = req.CreateResponse(HttpStatusCode.BadRequest);
                response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
                return response;
            }
        }

        private async Task<ResModel> StartConversation(string inputMsg, string token = "")
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
                    return BotReplyAsAPIResponse(responses);
                }

                return new ResModel() { MediaUrl = "", MediaType = "", Message = "", Actions = "" };
            }
        }

        public class ResModel
        {
            public required string MediaUrl { get; set; }
            public required string MediaType { get; set; }
            public required string Message { get; set; }
            public required string Actions { get; set; }
        }

        private static ResModel BotReplyAsAPIResponse(List<Activity> responses)
        {
            string responseStr = "";
            string responseMediaUrl = "";
            string responseMediaType = "";
            string responseAct = "";
            responses?.ForEach(responseActivity =>
            {
                // responseActivity is standard Microsoft.Bot.Connector.DirectLine.Activity
                // See https://github.com/Microsoft/botframework-sdk/blob/master/specs/botframework-activity/botframework-activity.md for reference
                // Showing examples of Text & SuggestedActions in response payload
                Console.WriteLine("responseActivity.Text: " + responseActivity.Text);
                if (!string.IsNullOrEmpty(responseActivity.Text))
                {
                    //responseStr = responseStr + string.Join(Environment.NewLine, responseActivity.Text);
                    responseStr = responseActivity.Text;
                }

                if (responseActivity.Attachments != null)
                {
                    foreach (Attachment attachment in responseActivity.Attachments)
                    {
                        Console.WriteLine("attachment.ContentType: " + attachment.ContentType);
                        var jsonStr = JsonConvert.SerializeObject(attachment.Content, Formatting.None);
                        Console.WriteLine("jsonStr: " + jsonStr);
                        dynamic jsonObj = JObject.Parse(jsonStr);
                        Console.WriteLine("jsonObj: " + jsonObj);
                        switch (attachment.ContentType)
                        {
                            case "application/vnd.microsoft.card.hero":
                                Console.WriteLine(jsonObj.images[0].url);
                                responseMediaUrl = jsonObj.images[0].url;
                                responseMediaType = "image";
                                break;

                            case "image/png":
                                Console.WriteLine($"Opening the requested image '{attachment.ContentUrl}'");
                                break;

                            case "application/vnd.microsoft.card.video":
                                Console.WriteLine(jsonObj.media[0].url);
                                responseMediaUrl = jsonObj.media[0].url;
                                responseMediaType = "video";
                                break;
                        }
                    }
                }

                if (responseActivity.SuggestedActions != null && responseActivity.SuggestedActions.Actions != null)
                {
                    //responseAct = responseActivity.SuggestedActions.Actions[0].Title;
                    //Console.WriteLine("Actions: " + responseAct);
                    var options = responseActivity.SuggestedActions?.Actions?.Select(a => a.Title.Trim()).ToList()?? new List<string>(0);
                    //responseStr = responseStr + $"\t{string.Join(" | ", options)}";
                    responseAct = $"\t{string.Join("|", options)}";
                }
            });

            Console.WriteLine("responseStr=> " + responseStr);
            ResModel responseObj = new ResModel() {
                MediaUrl = responseMediaUrl,
                MediaType = responseMediaType,
                Message = responseStr,
                Actions = responseAct
            };

            return responseObj;
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

                _watermark = response.Watermark;
                result = response.Activities?.Where(x =>
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
