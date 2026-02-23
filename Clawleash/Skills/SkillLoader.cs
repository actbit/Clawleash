using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Microsoft.Extensions.Logging;
using Clawleash.Models;

namespace Clawleash.Skills;

/// <summary>
/// スキルのロード・管理を行う
/// 指定フォルダーのJSON/YAMLファイルを自動スキャンしてロード
/// </summary>
public class SkillLoader : IDisposable
{
    private readonly ILogger<SkillLoader> _logger;
    private readonly string _skillsDirectory;
    private readonly Dictionary<string, Skill> _skills = new();
    private bool _disposed;
    private FileSystemWatcher? _watcher;

    public IReadOnlyDictionary<string, Skill> Skills => _skills;
    public string SkillsDirectory => _skillsDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public SkillLoader(
        ILogger<SkillLoader> logger,
        string? skillsDirectory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // スキルディレクトリ
        _skillsDirectory = skillsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clawleash", "Skills");
    }

    /// <summary>
    /// デフォルトスキルディレクトリの全スキルをロード
    /// </summary>
    public Task<int> LoadAllAsync(bool watchForChanges = false)
        => LoadAllFromDirectoryAsync(_skillsDirectory, watchForChanges);

    /// <summary>
    /// 指定ディレクトリ内の全スキルファイルをロード
    /// </summary>
    public async Task<int> LoadAllFromDirectoryAsync(string directory, bool watchForChanges = false)
    {
        Directory.CreateDirectory(directory);

        // YAML (.skill.yaml, .skill.yml) と JSON (.skill.json) をサポート
        var skillFiles = Directory.GetFiles(directory, "*.skill.yaml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(directory, "*.skill.yml", SearchOption.TopDirectoryOnly))
            .Concat(Directory.GetFiles(directory, "*.skill.json", SearchOption.TopDirectoryOnly))
            .ToArray();

        var loadedCount = 0;

        _logger.LogInformation("スキルディレクトリをスキャン: {Count} 個のスキルファイル", skillFiles.Length);

        foreach (var skillPath in skillFiles)
        {
            var result = await LoadFromFileAsync(skillPath);
            if (result != null)
            {
                loadedCount++;
            }
        }

        // ファイル変更監視を開始
        if (watchForChanges)
        {
            StartWatching(directory);
        }

        return loadedCount;
    }

    /// <summary>
    /// ファイル変更監視を開始
    /// </summary>
    private void StartWatching(string directory)
    {
        if (_watcher != null) return;

        // YAMLとJSON両方を監視
        _watcher = new FileSystemWatcher(directory)
        {
            Filter = "*.skill.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        _watcher.Created += async (s, e) =>
        {
            if (!IsSkillFile(e.FullPath)) return;
            _logger.LogInformation("新しいスキルを検出: {Path}", e.FullPath);
            await Task.Delay(500); // ファイル書き込み完了待ち
            await LoadFromFileAsync(e.FullPath);
        };

        _watcher.Changed += async (s, e) =>
        {
            if (!IsSkillFile(e.FullPath)) return;
            _logger.LogInformation("スキルが更新されました: {Path}", e.FullPath);
            await Task.Delay(500);
            // 再読み込み
            var skill = await LoadFromFileAsync(e.FullPath);
            if (skill != null)
            {
                _logger.LogInformation("スキルを再読み込み: {Name}", skill.Name);
            }
        };

        _watcher.Deleted += (s, e) =>
        {
            if (!IsSkillFile(e.FullPath)) return;
            var fileName = Path.GetFileNameWithoutExtension(e.FullPath);
            var skillName = fileName.Replace(".skill", "");

            // 名前で検索して削除
            var toRemove = _skills.FirstOrDefault(kvp =>
                kvp.Value.FilePath == e.FullPath || kvp.Key == skillName);

            if (toRemove.Value != null)
            {
                _logger.LogInformation("スキルが削除されたためアンロード: {Name}", toRemove.Key);
                _skills.Remove(toRemove.Key);
            }
        };

        _logger.LogInformation("スキルディレクトリの監視を開始: {Path}", directory);
    }

    /// <summary>
    /// スキルファイルかどうかを判定
    /// </summary>
    private static bool IsSkillFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var fileName = Path.GetFileName(path).ToLowerInvariant();
        return (ext == ".yaml" || ext == ".yml" || ext == ".json") &&
               fileName.Contains(".skill.");
    }

    /// <summary>
    /// ファイルからスキルをロード（JSON/YAML自動判定）
    /// </summary>
    public async Task<Skill?> LoadFromFileAsync(string skillPath)
    {
        if (!File.Exists(skillPath))
        {
            _logger.LogError("スキルファイルが見つかりません: {Path}", skillPath);
            return null;
        }

        try
        {
            _logger.LogDebug("スキルをロード中: {Path}", skillPath);

            var content = await File.ReadAllTextAsync(skillPath);
            var ext = Path.GetExtension(skillPath).ToLowerInvariant();

            Skill? skill;

            if (ext == ".yaml" || ext == ".yml")
            {
                skill = YamlDeserializer.Deserialize<Skill>(content);
            }
            else
            {
                skill = JsonSerializer.Deserialize<Skill>(content, JsonOptions);
            }

            if (skill == null || string.IsNullOrEmpty(skill.Name))
            {
                _logger.LogError("スキルの解析に失敗: {Path}", skillPath);
                return null;
            }

            skill.FilePath = skillPath;
            _skills[skill.Name] = skill;

            _logger.LogInformation("スキルロード完了: {Name} v{Version} ({Count} パラメータ)",
                skill.Name, skill.Version, skill.Parameters.Count);

            return skill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "スキルロードエラー: {Path}", skillPath);
            return null;
        }
    }

    /// <summary>
    /// YAML文字列からスキルをロード
    /// </summary>
    public Skill? LoadFromYaml(string yaml, string? name = null)
    {
        try
        {
            var skill = YamlDeserializer.Deserialize<Skill>(yaml);

            if (skill == null || string.IsNullOrEmpty(skill.Name))
            {
                _logger.LogError("スキルの解析に失敗");
                return null;
            }

            if (!string.IsNullOrEmpty(name))
            {
                skill.Name = name;
            }

            _skills[skill.Name] = skill;
            _logger.LogInformation("スキルを登録: {Name}", skill.Name);

            return skill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "スキル登録エラー (YAML)");
            return null;
        }
    }

    /// <summary>
    /// JSON文字列からスキルをロード
    /// </summary>
    public Skill? LoadFromJson(string json, string? name = null)
    {
        try
        {
            var skill = JsonSerializer.Deserialize<Skill>(json, JsonOptions);

            if (skill == null || string.IsNullOrEmpty(skill.Name))
            {
                _logger.LogError("スキルの解析に失敗");
                return null;
            }

            if (!string.IsNullOrEmpty(name))
            {
                skill.Name = name;
            }

            _skills[skill.Name] = skill;
            _logger.LogInformation("スキルを登録: {Name}", skill.Name);

            return skill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "スキル登録エラー (JSON)");
            return null;
        }
    }

    /// <summary>
    /// スキルを取得
    /// </summary>
    public Skill? GetSkill(string name)
    {
        return _skills.TryGetValue(name, out var skill) ? skill : null;
    }

    /// <summary>
    /// スキルを削除
    /// </summary>
    public bool RemoveSkill(string name)
    {
        if (_skills.Remove(name))
        {
            _logger.LogInformation("スキルを削除: {Name}", name);
            return true;
        }
        return false;
    }

    /// <summary>
    /// スキルの一覧を取得
    /// </summary>
    public IEnumerable<Skill> ListSkills(string? tag = null)
    {
        var skills = _skills.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(tag))
        {
            skills = skills.Where(s => s.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        }

        return skills;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _watcher?.Dispose();
        _watcher = null;
        _skills.Clear();
        _disposed = true;
    }
}
