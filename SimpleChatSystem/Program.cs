using Microsoft.KernelMemory.FileSystem.DevTools;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using Microsoft.KernelMemory;
using SimpleChatSystem;
using System.Net;
using System.Text;
using System.Text.Json;

var historyFolder = "chat_history";
var ollamaEndpoint = "http://127.0.0.1:11434";
var ollamaClient = new HttpClient
{
    BaseAddress = new Uri(ollamaEndpoint)
};
var modelName = await SelectOllamaModel(ollamaClient);
var kernelMemory = GetMemoryKernel(ollamaClient, modelName);

ChatRequest? chatRequest = SelectHistory(historyFolder);

await StartChat(ollamaClient, modelName, chatRequest, historyFolder);

static async Task<bool> ChatWithModel(HttpClient ollamaClient, ChatRequest chatRequest, string historyFolder, string modelName)
{
    Console.Write("User > ");

    var endOfConversation = false;
    var userInput = Console.ReadLine();

    if (userInput == "/bye" || string.IsNullOrWhiteSpace(userInput))
    {
        Console.WriteLine("Would you like to save your chat? Type /yes");

        var saveChat = Console.ReadLine();

        if (saveChat == "/yes")
        {
            var chatRequestJson = JsonSerializer.Serialize(chatRequest);
            var fileName = $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.json";
            var filePath = Path.Combine(historyFolder, fileName);

            File.WriteAllText(filePath, chatRequestJson);

            Console.WriteLine($"Chat saved to {filePath}");
        }

        return endOfConversation = false;
    }

    ChatResponse? chatResponse = null;

    if (await IsIntentToStoreAsync(ollamaClient, modelName, chatRequest, userInput))
    {
        // continue the conversation
        chatResponse = await ChatCompletion(ollamaClient, chatRequest, userInput);
    }
    else if (await IsQueryForMemory(ollamaClient, chatRequest, userInput, modelName))
    {
        // contiue the conversation
        endOfConversation = true;
    }

    return endOfConversation;
}

static async Task<bool> IsQueryForMemory(HttpClient ollamaClient, ChatRequest chatRequest, string userInput, string modelName)
{
    // get the kernel memeory
    var kernelMemory = GetMemoryKernel(ollamaClient, modelName);

    MemoryAnswer memoryAnswer = await kernelMemory.AskAsync(userInput);

    if (memoryAnswer != null && !memoryAnswer.NoResult)
    {
        Console.WriteLine("From Memory> " + memoryAnswer.Result.Trim());

        return await Task.FromResult(true);
    }

    return await Task.FromResult(false);
}

static async Task<bool> IsIntentToStoreAsync(HttpClient ollamaClient, string modelName, ChatRequest chatRequest, string userInput)
{
    var kernelMemory = GetMemoryKernel(ollamaClient, modelName);
    var systemChatMessage = chatRequest.Messages.FirstOrDefault(m => m.Role == "system");
    var intentChatRequest = new ChatRequest
    {
        Model = chatRequest.Model,
        Messages = new List<Message> { systemChatMessage },
        Stream = false
    };

    var llmResponse = string.Empty;

    if (string.IsNullOrEmpty(userInput))
    {
        return false;
    }

    var intentPrompt = await File.ReadAllTextAsync("prompts\\1.Intent.txt");
    var intentPromptWithUserInput = $"{intentPrompt} {userInput}";

    var chatResponse = await ChatCompletion(ollamaClient, intentChatRequest, intentPromptWithUserInput);

    if (chatResponse != null)
    {
        llmResponse = chatResponse.Message.Content;

        var jsonResponse = JsonExtractor.ExtractJsonFromLLMResponse(llmResponse);

        if (jsonResponse != null)
        {
            var memoryEntity = JsonSerializer.Deserialize<MemoryEntity>(jsonResponse);

            if (memoryEntity == null)
            {
                return await Task.FromResult(false);
            }

            if (memoryEntity.MemoryStructure.Tags[0].Equals("NO_MEMORY", StringComparison.InvariantCultureIgnoreCase))
            {
                return await Task.FromResult(false);
            }
            else
            {
                memoryEntity.Timestamp = DateTime.UtcNow;

                var tags = new TagCollection
                    {
                        { nameof(memoryEntity.EntityId), memoryEntity.EntityId },
                        { nameof(memoryEntity.MemoryStructure.Tags), string.Join("|", memoryEntity.MemoryStructure.Tags) },
                        { nameof(memoryEntity.Intent), memoryEntity.Intent },
                        { nameof(memoryEntity.Timestamp), memoryEntity.Timestamp.ToString() },
                        { nameof(memoryEntity.User), memoryEntity.User }
                    };

                var importedText = await kernelMemory.ImportTextAsync(memoryEntity.Context, tags: tags);

                Console.WriteLine($"Memory stored successfully: {importedText}");

                return await Task.FromResult(true);
            }
        }
    }

    return await Task.FromResult(false);
}


static async Task<string> SelectOllamaModel(HttpClient ollamaClient)
{
    var responseMessage = await ollamaClient.GetAsync("/api/tags");
    var content = await responseMessage.Content.ReadAsStringAsync();

    if (responseMessage != null && responseMessage.StatusCode == HttpStatusCode.OK && content != null)
    {
        ModelsResponse modelsResponse = JsonSerializer.Deserialize<ModelsResponse>(content)!;

        if (modelsResponse != null)
        {
            for (int i = 0; i < modelsResponse.Models.Count; i++)
            {
                Model? model = modelsResponse.Models[i];
                Console.WriteLine($"({i}) {model.Name}");
            }

            Console.WriteLine();
            Console.WriteLine("Please use the numeric value for the model to interact with.");

            var userInput = Console.ReadLine();

            if (!int.TryParse(userInput, out int modelIndex) || modelIndex < 0 || modelIndex >= modelsResponse.Models.Count)
            {
                Console.WriteLine("Invalid model index.");

                return string.Empty;
            }

            return modelsResponse.Models[modelIndex].Name;
        }
    }

    return string.Empty;
}

static async Task StartChat(HttpClient ollamaClient, string modelName, ChatRequest? chatRequest, string historyFolder)
{
    if (modelName != string.Empty)
    {
        // chat with the model
        Console.Clear();
        Console.WriteLine("Chatting with the model...");
        Console.WriteLine("To end the chat type /bye");
        Console.WriteLine();

        if (chatRequest == null)
        {
            chatRequest = new ChatRequest
            {
                Model = modelName,
                Messages = [],
                Stream = false
            };

            var userMessage = new Message { Role = "system", Content = "You are a helpfull assistant." };

            chatRequest.Messages.Add(userMessage);
        }

        while (await ChatWithModel(ollamaClient, chatRequest, historyFolder, modelName)) ;
    }
}

static async Task<ChatResponse?> ChatCompletion(HttpClient ollamaClient, ChatRequest chatRequest, string userInput)
{
    var userMessage = new Message { Role = "user", Content = userInput };

    chatRequest.Messages.Add(userMessage);

    var chatRequestJson = JsonSerializer.Serialize(chatRequest);
    var content = new StringContent(chatRequestJson, Encoding.UTF8, "application/json");
    var responseMessage = await ollamaClient.PostAsync("/api/chat", content);
    var llmResponse = await responseMessage.Content.ReadAsStringAsync();
    var chatResponse = JsonSerializer.Deserialize<ChatResponse>(llmResponse);

    return chatResponse;
}

static ChatRequest? SelectHistory(string historyFolder)
{
    // check to see if we we have history
    if (!Directory.Exists(historyFolder))
    {
        Directory.CreateDirectory(historyFolder);

        Console.WriteLine("No chat history found.");

        return null;
    }

    string[] files = Directory.GetFiles(historyFolder);

    if (files.Length == 0)
    {
        Console.WriteLine("No chat history found.");

        return null;
    }

    Console.WriteLine("Select a chat history to load, if starting a new chat type /new");

    for (int i = 0; i < files.Length; i++)
    {
        Console.WriteLine($"({i}) {files[i]}");
    }

    while (true)
    {
        var userInput = Console.ReadLine();

        if (userInput == "/new")
        {
            return null;
        }

        if (!int.TryParse(userInput, out int fileIndex) || fileIndex < 0 || fileIndex >= files.Length)
        {
            Console.WriteLine("Invalid file index.");

            continue;
        }

        var file = files[fileIndex];

        return JsonSerializer.Deserialize<ChatRequest>(File.ReadAllText(file));
    }
}

static MemoryServerless GetMemoryKernel(HttpClient ollamaClient, string modelName)
{
    var memoryBuilder = new KernelMemoryBuilder();

    memoryBuilder.WithCustomPromptProvider(new OllamaPromptProvider());
    memoryBuilder.WithCustomEmbeddingGenerator(new OllamaTextEmbedding())
                .WithCustomTextGenerator(new OllamaTextGeneration(ollamaClient, modelName))
                .WithSimpleVectorDb(new SimpleVectorDbConfig { Directory = "VectorDirectory", StorageType = FileSystemTypes.Disk })
                .Build<MemoryServerless>();

    var memory = memoryBuilder.Build<MemoryServerless>();

    return memory;
}