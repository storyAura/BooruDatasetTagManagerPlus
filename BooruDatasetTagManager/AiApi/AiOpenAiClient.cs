using Diffusion.IO;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager.AiApi
{
    public class AiOpenAiClient
    {
        public string ServerEndpoint { get; private set; }
        //public event Extensions.ErrorHandler ErrorMessage;

        // All chat clients for one model, one per API key; the whole set is
        // swapped atomically when the requested model changes.
        private sealed class ChatClientSet
        {
            public string Model;
            public ChatClient[] Clients;
        }

        private ChatClientSet chatClients = null;
        private readonly ApiKeyCredential[] credentials;
        private int keyRotation = -1;
        private OpenAIClientOptions serverOptions;

        public bool IsConnected { get; private set; }


        public List<string> Models { get; private set; }

        public AiOpenAiClient(string srvEndpoint, string apiKey, int timeout)
            : this(srvEndpoint, new[] { apiKey }, timeout)
        {
        }

        public AiOpenAiClient(string srvEndpoint, IEnumerable<string> apiKeys, int timeout)
        {
            ServerEndpoint = srvEndpoint;
            List<string> keys = LlmApiProfileLogic.SanitizeTokens(apiKeys);
            if (keys.Count == 0)
                keys.Add("not-required");
            credentials = keys.Select(key => new ApiKeyCredential(key)).ToArray();
            serverOptions = new OpenAIClientOptions();
            serverOptions.Endpoint = new Uri(ServerEndpoint);
            serverOptions.NetworkTimeout = TimeSpan.FromSeconds(timeout);
            Models = new List<string>();

        }

        /// <summary>Client for the active LLM site profile (all its keys rotate per request).</summary>
        public static AiOpenAiClient CreateFromSettings(AppSettings settings)
        {
            return new AiOpenAiClient(
                settings.OpenAiAutoTagger.ConnectionAddress,
                settings.GetActiveLlmApiKeys(),
                settings.OpenAiAutoTagger.RequestTimeout);
        }

        public async Task<(bool Result, string ErrMessage)> ConnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                OpenAIModelClient client = new OpenAIModelClient(credentials[0], serverOptions);
                var models = await client.GetModelsAsync(cancellationToken);
                Models.Clear();
                Models.AddRange(models.Value.Select(a => a.Id).Order());
                IsConnected = true;
                return (true, "");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, GetFriendlyConnectionError(ex.Message));
            }
        }

        public async Task<(string Result, string ErrMessage)> SendRequestAsync(
            OpenAiRequest request,
            CancellationToken cancellationToken = default)
        {
            OpenAiDetailedResponse response = await SendDetailedRequestAsync(request, cancellationToken).ConfigureAwait(false);
            return (response.Result, response.ErrMessage);
        }

        public async Task<OpenAiDetailedResponse> SendDetailedRequestAsync(
            OpenAiRequest request,
            CancellationToken cancellationToken = default)
        {
            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                // Snapshot the shared field into a local: two concurrent requests
                // for different models could otherwise send on each other's
                // freshly-swapped clients (this instance is shared app-wide).
                ChatClientSet set = chatClients;
                if (set == null || set.Model != request.Model)
                {
                    set = new ChatClientSet
                    {
                        Model = request.Model,
                        Clients = credentials.Select(c => new ChatClient(request.Model, c, serverOptions)).ToArray()
                    };
                    chatClients = set;
                }
                // Multiple saved API keys rotate across requests (round-robin).
                ChatClient client = set.Clients[
                    LlmApiProfileLogic.RotationIndex(Interlocked.Increment(ref keyRotation), set.Clients.Length)];
                List<ChatMessage> messages = new List<ChatMessage>();
                var chatOptions = new ChatCompletionOptions();
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                    messages.Add(new SystemChatMessage(request.SystemPrompt));
                UserChatMessage userMessage = new UserChatMessage(request.UserPrompt);
                foreach (var item in request.ImagePath)
                {
                    BinaryData bd = new BinaryData(await File.ReadAllBytesAsync(item, cancellationToken));
                    string contentType = GetContentTypeFromExtention(Path.GetExtension(item));
                    ChatMessageContentPart partImage = ChatMessageContentPart.CreateImagePart(bd, contentType);
                    userMessage.Content.Add(partImage);
                }
                foreach (var item in request.ImageData)
                {
                    BinaryData bd = new BinaryData(item);
                    ChatMessageContentPart partImage = ChatMessageContentPart.CreateImagePart(bd, request.ContentType);
                    userMessage.Content.Add(partImage);
                }
                messages.Add(userMessage);
                bool useChatOptions = false;
                if (request.RepeatPenalty != 0 || request.TopP != -1 || request.Temperature != -1)
                {
                    useChatOptions = true;
                    if (request.Temperature != -1)
                        chatOptions.Temperature = request.Temperature;
                    if (request.TopP != -1)
                        chatOptions.TopP = request.TopP;
                    if (request.RepeatPenalty != 0)
                        chatOptions.FrequencyPenalty = request.RepeatPenalty;
                }
                ChatCompletion result;
                result = await client.CompleteChatAsync(
                    messages,
                    useChatOptions ? chatOptions : null,
                    cancellationToken);
                if (result.FinishReason != ChatFinishReason.Stop)
                {
                    timer.Stop();
                    return new OpenAiDetailedResponse(
                        null,
                        "OpenAiClient SendRequest return error: " + result.FinishReason.ToString(),
                        timer.Elapsed);
                }
                timer.Stop();
                return new OpenAiDetailedResponse(
                    result.Content[0].Text,
                    string.Empty,
                    timer.Elapsed,
                    result.Usage?.InputTokenCount,
                    result.Usage?.OutputTokenCount,
                    result.Usage?.TotalTokenCount);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                timer.Stop();
                return new OpenAiDetailedResponse(null, "OpenAiClient SendRequest error: " + ex.Message, timer.Elapsed);
            }
        }

        private string GetContentTypeFromExtention(string ext)
        {
            switch (ext.ToLower())
            {
                case ".jpg":
                    {
                        return "image/jpeg";
                    }
                case ".bmp":
                    {
                        return "image/bmp";
                    }
                case ".png":
                    {
                        return "image/png";
                    }
                case ".gif":
                    {
                        return "image/gif";
                    }
                case ".webp":
                    {
                        return "image/webp";
                    }
                default:
                    {
                        return "application/octet-stream";
                    }
            }
        }

        public async Task<(List<AutoTagItem> data, string errorMessage, bool canceled)> GetTagsWithAutoTagger(
            string imagePath,
            bool defSettings,
            CancellationToken cancellationToken = default)
        {
            if (!defSettings || Program.OpenAiAutoTagger == null || string.IsNullOrEmpty(Program.Settings.OpenAiAutoTagger.ResolveVisionModel()))
            {
                Form_AutoTaggerOpenAiSettings autoTaggerSettings = new Form_AutoTaggerOpenAiSettings();
                if (autoTaggerSettings.ShowDialog() != DialogResult.OK || Program.OpenAiAutoTagger == null || string.IsNullOrEmpty(Program.Settings.OpenAiAutoTagger.ResolveVisionModel()))
                {
                    autoTaggerSettings.Close();
                    return (null, I18n.GetText("TipGenCancel"), true);
                }
            }
            if (!Program.OpenAiAutoTagger.IsConnected)
            {
                var connectionResult = await Program.OpenAiAutoTagger.ConnectAsync(cancellationToken);
                if (!connectionResult.Result)
                {
                    return (null, connectionResult.ErrMessage, true);
                }
            }

            OpenAiRequest request = new OpenAiRequest();
            request.Temperature = Program.Settings.OpenAiAutoTagger.Temperature;
            request.UserPrompt = Program.Settings.OpenAiAutoTagger.UserPrompt;
            request.TopP = Program.Settings.OpenAiAutoTagger.TopP;
            AiPromptTemplateLibrary promptLibrary = AiPromptTemplateLibrary.Create(
                Program.Settings.AiServerSetPromptTemplates,
                Program.Settings.AiServerSetPromptTemplateId,
                Program.Settings.AiServerSetPromptTemplate);
            request.SystemPrompt = promptLibrary.SelectedTemplate.SystemPrompt;
            request.Model = Program.Settings.OpenAiAutoTagger.ResolveVisionModel();
            request.RepeatPenalty = Program.Settings.OpenAiAutoTagger.RepeatPenalty;
            string imgExt = Path.GetExtension(imagePath).ToLower();
            if (imgExt == ".webp")
            {
                request.ImageData.Add(Extensions.ImageToByteArray(Extensions.GetImageFromFile(imagePath)));
                request.ContentType = "image/png";
            }
            else if (Extensions.VideoExtensions.Contains(imgExt))
            {
                var images = Extensions.GetImagesFromVideo(imagePath, Program.Settings.OpenAiAutoTagger.VideoFrameCount, Program.Settings.OpenAiAutoTagger.VideoFrameScale);
                foreach (var item in images)
                {
                    request.ImageData.Add(Extensions.ImageToByteArray(item.Value));
                }
                request.ContentType = "image/png";
            }
            else
                request.ImagePath.Add(imagePath);


            var response = await Program.OpenAiAutoTagger.SendRequestAsync(request, cancellationToken);
            string errMess = response.ErrMessage;
            if (response.Result == null)
            {
                return (null, errMess, false);
            }
            response.Result = RemoveThinking(response.Result);
            List<AutoTagItem> result = new List<AutoTagItem>();
            if (Program.Settings.OpenAiAutoTagger.SplitString)
            {
                result = response.Result.Split(Program.Settings.OpenAiAutoTagger.Splitter, StringSplitOptions.RemoveEmptyEntries).Select(a=>new AutoTagItem(a.Trim(), 1f)).ToList();
            }
            else
            {
                result.Add(new AutoTagItem(response.Result, 1f));
            }

            if (Program.Settings.OpenAiAutoTagger.TagFilteringMode != TagFilteringMode.None && !string.IsNullOrEmpty(Program.Settings.OpenAiAutoTagger.TagFilter))
            {
                if (Program.Settings.OpenAiAutoTagger.TagFilteringMode == TagFilteringMode.Regex)
                    try
                    {
                        result = result.Where(t => Regex.IsMatch(t.Tag, Program.Settings.OpenAiAutoTagger.TagFilter, RegexOptions.IgnoreCase)).ToList();
                    }
                    catch
                    {
                        errMess = I18n.GetText("TipInvalidRegex");
                    }
                else
                {
                    string[] tagFilter = Program.Settings.OpenAiAutoTagger.TagFilter.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (Program.Settings.OpenAiAutoTagger.TagFilteringMode == TagFilteringMode.Equal)
                        result = result.Where(t => tagFilter.Any(f => string.Equals(t.Tag, f, StringComparison.OrdinalIgnoreCase))).ToList();
                    else if (Program.Settings.OpenAiAutoTagger.TagFilteringMode == TagFilteringMode.NotEqual)
                        result = result.Where(t => !tagFilter.Any(f => string.Equals(t.Tag, f, StringComparison.OrdinalIgnoreCase))).ToList();
                    else if (Program.Settings.OpenAiAutoTagger.TagFilteringMode == TagFilteringMode.Containing)
                        result = result.Where(t => tagFilter.Any(f => t.Tag.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
                    else if (Program.Settings.OpenAiAutoTagger.TagFilteringMode == TagFilteringMode.NotContaining)
                        result = result.Where(t => !tagFilter.Any(f => t.Tag.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
                }
            }

            if (Program.Settings.OpenAiAutoTagger.SortMode == AutoTaggerSort.Confidence)
            {
                result.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
            }
            else if (Program.Settings.OpenAiAutoTagger.SortMode == AutoTaggerSort.Alphabetical)
            {
                result.Sort((a, b) => a.Tag.CompareTo(b.Tag));
            }
            return (result, errMess, false);
        }

        private string RemoveThinking(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;
            string pattern = @"<think>.*?(?:</think>|$)";
            string result = Regex.Replace(text, pattern, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return result.Trim(['\n', '\r']).Trim();
        }

        private static string GetFriendlyConnectionError(string error)
        {
            switch (OpenAiConnectionErrorClassifier.Classify(error))
            {
                case OpenAiConnectionErrorKind.InvalidApiResponse:
                    return I18n.GetText("AiServerSetInvalidApiResponse");
                case OpenAiConnectionErrorKind.Authentication:
                    return I18n.GetText("AiServerSetAuthenticationError");
                case OpenAiConnectionErrorKind.Timeout:
                    return I18n.GetText("AiServerSetConnectionTimeout");
                case OpenAiConnectionErrorKind.Network:
                    return I18n.GetText("AiServerSetNetworkError");
                default:
                    return "OpenAiClient connection error: " + error;
            }
        }
    }

    public sealed class OpenAiDetailedResponse
    {
        public OpenAiDetailedResponse(
            string result,
            string errMessage,
            TimeSpan duration,
            int? inputTokens = null,
            int? outputTokens = null,
            int? totalTokens = null)
        {
            Result = result;
            ErrMessage = errMessage ?? string.Empty;
            Duration = duration;
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
            TotalTokens = totalTokens;
        }

        public string Result { get; }
        public string ErrMessage { get; }
        public TimeSpan Duration { get; }
        public int? InputTokens { get; }
        public int? OutputTokens { get; }
        public int? TotalTokens { get; }
        public bool HasUsage => InputTokens.HasValue && OutputTokens.HasValue && TotalTokens.HasValue;
    }



    public class OpenAiRequest
    {
        public string Model { get; set; } = null;
        public string SystemPrompt { get; set; } = null;
        public string UserPrompt { get; set; } = string.Empty;
        public List<string> ImagePath { get; set; }
        public List<byte[]> ImageData { get; set; }
        public string ContentType { get; set; } = null;
        public float Temperature { get; set; } = -1;
        public float TopP { get; set; } = -1;
        public float RepeatPenalty { get; set; } = 0;

        public OpenAiRequest()
        {
            ImagePath = new List<string>();
            ImageData = new List<byte[]>();
        }

    }
}
