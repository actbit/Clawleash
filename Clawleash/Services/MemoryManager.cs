using System.Collections.Concurrent;
using System.Text.Json;

namespace Clawleash.Services;

/// <summary>
/// メモリ管理サービス
/// エージェントの短期記憶・長期記憶を管理
/// </summary>
public class MemoryManager : IDisposable
{
    private readonly ConcurrentQueue<MemoryItem> _shortTermMemory = new();
    private readonly ConcurrentDictionary<string, MemoryItem> _longTermMemory = new();
    private readonly ConcurrentDictionary<string, List<string>> _contextIndex = new();
    private readonly int _maxShortTermMemory = 50;
    private readonly string _memoryFilePath;
    private readonly Timer? _persistTimer;
    private bool _disposed;

    public MemoryManager(string? memoryDirectory = null)
    {
        var dir = memoryDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clawleash", "memory");
        Directory.CreateDirectory(dir);
        _memoryFilePath = Path.Combine(dir, "memory.json");

        LoadMemory();

        // 定期的にメモリを永続化
        _persistTimer = new Timer(_ => SaveMemory(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    #region 短期記憶

    /// <summary>
    /// 短期記憶に追加（現在のセッションの会話など）
    /// </summary>
    public void AddToShortTermMemory(string content, string type, Dictionary<string, object>? metadata = null)
    {
        var item = new MemoryItem
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Content = content,
            Type = type,
            Metadata = metadata ?? new Dictionary<string, object>(),
            Timestamp = DateTime.UtcNow,
            Importance = CalculateImportance(content, type)
        };

        _shortTermMemory.Enqueue(item);

        // 上限を超えた場合は古いものから削除
        while (_shortTermMemory.Count > _maxShortTermMemory)
        {
            _shortTermMemory.TryDequeue(out _);
        }

        // 重要度が高いものは長期記憶にも追加
        if (item.Importance >= 0.7)
        {
            AddToLongTermMemory(item);
        }
    }

    /// <summary>
    /// 短期記憶を取得
    /// </summary>
    public List<MemoryItem> GetShortTermMemory(int? limit = null)
    {
        var items = _shortTermMemory.ToList();
        return limit.HasValue ? items.TakeLast(limit.Value).ToList() : items;
    }

    /// <summary>
    /// 短期記憴をクリア
    /// </summary>
    public void ClearShortTermMemory()
    {
        while (_shortTermMemory.TryDequeue(out _)) { }
    }

    #endregion

    #region 長期記憶

    /// <summary>
    /// 長期記憶に追加（重要な情報、ユーザー設定など）
    /// </summary>
    public void AddToLongTermMemory(MemoryItem item)
    {
        _longTermMemory[item.Id] = item;

        // インデックスを更新
        var keywords = ExtractKeywords(item.Content);
        foreach (var keyword in keywords)
        {
            if (!_contextIndex.ContainsKey(keyword))
            {
                _contextIndex[keyword] = new List<string>();
            }
            if (!_contextIndex[keyword].Contains(item.Id))
            {
                _contextIndex[keyword].Add(item.Id);
            }
        }
    }

    /// <summary>
    /// 長期記憶から検索
    /// </summary>
    public List<MemoryItem> SearchLongTermMemory(string query, int limit = 5)
    {
        var keywords = ExtractKeywords(query);
        var matchedIds = new Dictionary<string, int>();

        foreach (var keyword in keywords)
        {
            if (_contextIndex.TryGetValue(keyword, out var ids))
            {
                foreach (var id in ids)
                {
                    if (!matchedIds.ContainsKey(id))
                    {
                        matchedIds[id] = 0;
                    }
                    matchedIds[id]++;
                }
            }
        }

        return matchedIds
            .OrderByDescending(x => x.Value)
            .Take(limit)
            .Select(x => _longTermMemory.GetValueOrDefault(x.Key))
            .Where(x => x != null)
            .Cast<MemoryItem>()
            .ToList();
    }

    /// <summary>
    /// 長期記憶を取得
    /// </summary>
    public List<MemoryItem> GetLongTermMemory(string? type = null)
    {
        var items = _longTermMemory.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(type))
        {
            items = items.Where(x => x.Type == type);
        }

        return items.OrderByDescending(x => x.Importance).ToList();
    }

    /// <summary>
    /// 長期記憶から削除
    /// </summary>
    public void RemoveFromLongTermMemory(string id)
    {
        if (_longTermMemory.TryRemove(id, out var item))
        {
            var keywords = ExtractKeywords(item.Content);
            foreach (var keyword in keywords)
            {
                if (_contextIndex.TryGetValue(keyword, out var ids))
                {
                    ids.Remove(id);
                }
            }
        }
    }

    #endregion

    #region コンテキスト管理

    /// <summary>
    /// 現在のコンテキストを構築（AIに渡す用）
    /// </summary>
    public string BuildContext(string? query = null, int maxLength = 4000)
    {
        var context = new System.Text.StringBuilder();

        // 長期記憴から関連情報を取得
        if (!string.IsNullOrEmpty(query))
        {
            var relevantMemories = SearchLongTermMemory(query, 3);
            if (relevantMemories.Count > 0)
            {
                context.AppendLine("## 関連する過去の情報");
                foreach (var memory in relevantMemories)
                {
                    context.AppendLine($"- [{memory.Timestamp:yyyy-MM-dd}] {memory.Content}");
                }
                context.AppendLine();
            }
        }

        // 最近の短期記憶
        var recentMemories = GetShortTermMemory(10);
        if (recentMemories.Count > 0)
        {
            context.AppendLine("## 最近の操作履歴");
            foreach (var memory in recentMemories)
            {
                var truncated = memory.Content.Length > 200 ? memory.Content[..200] + "..." : memory.Content;
                context.AppendLine($"- [{memory.Type}] {truncated}");
            }
        }

        var result = context.ToString();
        return result.Length > maxLength ? result[..maxLength] + "..." : result;
    }

    /// <summary>
    /// 会話履歴を記録
    /// </summary>
    public void RecordConversation(string role, string content)
    {
        AddToShortTermMemory(content, $"conversation_{role}", new Dictionary<string, object>
        {
            ["role"] = role,
            ["length"] = content.Length
        });
    }

    /// <summary>
    /// アクションを記録
    /// </summary>
    public void RecordAction(string action, string result, bool success)
    {
        AddToShortTermMemory($"{action}: {result}", "action", new Dictionary<string, object>
        {
            ["action"] = action,
            ["success"] = success
        });
    }

    /// <summary>
    /// ユーザー設定を保存
    /// </summary>
    public void SaveUserPreference(string key, string value)
    {
        var item = new MemoryItem
        {
            Id = $"pref_{key}",
            Content = value,
            Type = "preference",
            Metadata = new Dictionary<string, object> { ["key"] = key },
            Timestamp = DateTime.UtcNow,
            Importance = 1.0
        };
        AddToLongTermMemory(item);
    }

    /// <summary>
    /// ユーザー設定を取得
    /// </summary>
    public string? GetUserPreference(string key)
    {
        var id = $"pref_{key}";
        return _longTermMemory.GetValueOrDefault(id)?.Content;
    }

    #endregion

    #region プライベートメソッド

    private static double CalculateImportance(string content, string type)
    {
        var importance = 0.5;

        // タイプに基づく重要度
        importance += type switch
        {
            "preference" => 0.5,
            "error" => 0.3,
            "milestone" => 0.4,
            "conversation_user" => 0.2,
            _ => 0
        };

        // キーワードに基づく重要度
        var importantKeywords = new[] { "重要", "重要", "設定", "password", "api key", "承認", "エラー", "失敗" };
        foreach (var keyword in importantKeywords)
        {
            if (content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                importance += 0.1;
            }
        }

        return Math.Min(importance, 1.0);
    }

    private static List<string> ExtractKeywords(string content)
    {
        var keywords = new List<string>();
        var words = content.Split(new[] { ' ', '　', '\n', '\r', ',', '.', '、', '。' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            if (word.Length >= 2 && word.Length <= 20)
            {
                keywords.Add(word.ToLowerInvariant());
            }
        }

        return keywords.Distinct().Take(10).ToList();
    }

    private void SaveMemory()
    {
        try
        {
            var data = new MemoryData
            {
                ShortTermMemory = _shortTermMemory.ToList(),
                LongTermMemory = _longTermMemory.ToDictionary()
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_memoryFilePath, json);
        }
        catch
        {
            // 保存エラーは無視
        }
    }

    private void LoadMemory()
    {
        try
        {
            if (!File.Exists(_memoryFilePath))
            {
                return;
            }

            var json = File.ReadAllText(_memoryFilePath);
            var data = JsonSerializer.Deserialize<MemoryData>(json);

            if (data != null)
            {
                foreach (var item in data.ShortTermMemory)
                {
                    _shortTermMemory.Enqueue(item);
                }

                foreach (var kvp in data.LongTermMemory)
                {
                    _longTermMemory[kvp.Key] = kvp.Value;
                }
            }
        }
        catch
        {
            // 読み込みエラーは無視
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _persistTimer?.Dispose();
        SaveMemory();
        _disposed = true;
    }
}

/// <summary>
/// メモリアイテム
/// </summary>
public class MemoryItem
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime Timestamp { get; set; }
    public double Importance { get; set; }
}

/// <summary>
/// メモリデータ（シリアライズ用）
/// </summary>
internal class MemoryData
{
    public List<MemoryItem> ShortTermMemory { get; set; } = [];
    public Dictionary<string, MemoryItem> LongTermMemory { get; set; } = new();
}
