using System.Text.Json;
using System.Text.Json.Serialization;
using Clipy.Localization;
using Clipy.Models;

namespace Clipy.Services;

public sealed class ChatHistoryService
{
    private static readonly string ChatsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipyAssistant", "chats");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public ChatSession CreateSession(string workspace)
    {
        var session = new ChatSession
        {
            Workspace = workspace,
            Title = Loc.Get("chat.new"),
        };
        Save(session);
        return session;
    }

    public void Save(ChatSession session)
    {
        session.UpdatedAt = DateTime.UtcNow;
        Directory.CreateDirectory(ChatsDir);
        var path = Path.Combine(ChatsDir, $"{session.Id}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(session, JsonOpts));
    }

    public ChatSession? Load(string id)
    {
        try
        {
            var path = Path.Combine(ChatsDir, $"{id}.json");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ChatSession>(File.ReadAllText(path), JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<ChatSession> List()
    {
        try
        {
            if (!Directory.Exists(ChatsDir)) return Array.Empty<ChatSession>();
            return Directory.GetFiles(ChatsDir, "*.json")
                .Select(f =>
                {
                    try
                    {
                        return JsonSerializer.Deserialize<ChatSession>(File.ReadAllText(f), JsonOpts);
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(s => s is not null)
                .Cast<ChatSession>()
                .OrderByDescending(s => s.UpdatedAt)
                .ToList();
        }
        catch
        {
            return Array.Empty<ChatSession>();
        }
    }

    public void Delete(string id)
    {
        try
        {
            var path = Path.Combine(ChatsDir, $"{id}.json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* ignore */ }
    }

    public static string MakeTitle(string firstUserMessage)
    {
        var t = firstUserMessage.Replace('\n', ' ').Trim();
        if (t.Length > 48) t = t[..45] + "…";
        return string.IsNullOrWhiteSpace(t) ? Loc.Get("chat.new") : t;
    }
}
