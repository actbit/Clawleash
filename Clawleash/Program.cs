using Clawleash.Configuration;
using Clawleash.Models;
using Clawleash.Plugins;
using Clawleash.Sandbox;
using Clawleash.Security;
using Clawleash.Services;
using Clawleash.Skills;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Clawleash;

/// <summary>
/// Clawleash - OpenClow風の自律エージェント
/// Semantic Kernel Agent Frameworkとサンドボックス環境による安全な実行を提供
/// </summary>
internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("================================");
        Console.WriteLine("   Clawleash Agent v1.0");
        Console.WriteLine("   OpenClow-style AI Agent");
        Console.WriteLine("================================\n");

        try
        {
            // 設定を読み込み
            var settings = LoadSettings();

            // API Keyの確認
            if (string.IsNullOrEmpty(settings.AI.ApiKey))
            {
                Console.WriteLine("エラー: appsettings.json で AI.ApiKey を設定してください");
                Console.WriteLine("環境変数 CLAWLEASH_API_KEY でも設定可能です");

                // 環境変数から取得を試行
                var envApiKey = Environment.GetEnvironmentVariable("CLAWLEASH_API_KEY");
                if (!string.IsNullOrEmpty(envApiKey))
                {
                    settings.AI.ApiKey = envApiKey;
                    Console.WriteLine("環境変数からAPI Keyを取得しました");
                }
                else
                {
                    Console.Write("\nAPI Keyを入力してください: ");
                    var inputKey = Console.ReadLine();
                    if (string.IsNullOrEmpty(inputKey))
                    {
                        return;
                    }
                    settings.AI.ApiKey = inputKey;
                }
            }

            // DIコンテナを構築
            var serviceProvider = ConfigureServices(settings);

            // エージェントを作成
            var agent = CreateAgent(serviceProvider, settings);

            // チャットインターフェースを開始
            await RunChatInterfaceAsync(serviceProvider, agent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n致命的エラー: {ex.Message}");
            Console.WriteLine($"スタックトレース: {ex.StackTrace}");
        }
    }

    private static ClawleashSettings LoadSettings()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var settings = new ClawleashSettings();
        configuration.Bind(settings);

        return settings;
    }

    private static IServiceProvider ConfigureServices(ClawleashSettings settings)
    {
        var services = new ServiceCollection();

        // 設定を登録
        services.AddSingleton(settings);

        // セキュリティ検証クラスを登録
        services.AddSingleton<PathValidator>();
        services.AddSingleton<CommandValidator>();
        services.AddSingleton<UrlValidator>();

        // サンドボックスプロバイダーを登録
        services.AddSingleton<ISandboxProvider>(sp =>
            SandboxFactory.Create(settings));

        // サービスを登録
        services.AddSingleton<IPowerShellExecutor, PowerShellExecutor>();
        services.AddSingleton<IBrowserManager, BrowserManager>();

        // 自律エージェント設定を登録
        services.AddSingleton(new AutonomousSettings
        {
            MaxSteps = 10,
            MaxRetries = 3,
            RequireApprovalForDangerousOperations = true,
            RequireApprovalForFileDeletion = true,
            RequireApprovalForFormSubmission = true,
            StepDelayMs = 500
        });

        // スキルローダーを登録
        services.AddSingleton<SkillLoader>();

        // Semantic Kernelを登録
        services.AddSingleton<Kernel>(sp => BuildKernel(sp, settings));

        // チャットインターフェースを登録
        services.AddSingleton<IChatInterface, CliChatInterface>();
        services.AddSingleton<ChatInterfaceManager>();

        return services.BuildServiceProvider();
    }

    private static Kernel BuildKernel(IServiceProvider serviceProvider, ClawleashSettings settings)
    {
        var builder = Kernel.CreateBuilder();

        // OpenAI互換APIを設定
        builder.AddOpenAIChatCompletion(
            modelId: settings.AI.ModelId,
            apiKey: settings.AI.ApiKey,
            endpoint: new Uri(settings.AI.Endpoint));

        // サービスを取得
        var pathValidator = serviceProvider.GetRequiredService<PathValidator>();
        var powerShellExecutor = serviceProvider.GetRequiredService<IPowerShellExecutor>();
        var browserManager = serviceProvider.GetRequiredService<IBrowserManager>();
        var autonomousSettings = serviceProvider.GetRequiredService<AutonomousSettings>();
        var skillLoader = serviceProvider.GetRequiredService<SkillLoader>();

        // プラグインを明示的にインスタンス化して登録
        var kernel = builder.Build();
        kernel.Plugins.AddFromObject(new RestrictedFileSystemPlugin(pathValidator), "FileSystem");
        kernel.Plugins.AddFromObject(new FileOperationsPlugin(pathValidator), "FileOperations");
        kernel.Plugins.AddFromObject(new RestrictedPowerShellPlugin(powerShellExecutor), "PowerShell");
        kernel.Plugins.AddFromObject(new RestrictedBrowserPlugin(browserManager), "Browser");
        kernel.Plugins.AddFromObject(new WebCrawlerPlugin(browserManager), "WebCrawler");
        kernel.Plugins.AddFromObject(new BrowserActionsPlugin(browserManager), "BrowserActions");
        kernel.Plugins.AddFromObject(new AdvancedBrowserPlugin(browserManager), "AdvancedBrowser");
        kernel.Plugins.AddFromObject(new StructuredDataExtractionPlugin(browserManager, kernel), "DataExtraction");
        kernel.Plugins.AddFromObject(new AutonomousAgentPlugin(kernel, autonomousSettings), "AutonomousAgent");

        // スキルプラグインを登録してスキルをロード
        kernel.Plugins.AddFromObject(new SkillPlugin(skillLoader, kernel), "Skills");

        // スキルをロード（非同期だが、ここでは同期的に実行）
        Task.Run(async () =>
        {
            var loaded = await skillLoader.LoadAllAsync(watchForChanges: true);
            Console.WriteLine($"スキルを {loaded} 件ロードしました: {skillLoader.SkillsDirectory}");
        }).Wait();

        return kernel;
    }

    private static ChatCompletionAgent CreateAgent(IServiceProvider serviceProvider, ClawleashSettings settings)
    {
        var kernel = serviceProvider.GetRequiredService<Kernel>();

        var agent = new ChatCompletionAgent
        {
            Name = "ClawleashAgent",
            Instructions = """
                あなたはClawleash（クラウリッシュ）エージェントです。
                OpenClow風の自律的なAIアシスタントとして、ユーザーのタスクを支援します。
                Firecrawl/OpenCraw風のWebスクレイピング・クローリング機能と高度なブラウザ操作機能を備えています。

                ## 能力
                ### ファイル操作
                - 許可されたディレクトリ内でのファイル読み書き、一覧取得

                ### PowerShell実行
                - 許可されたコマンドの実行

                ### ブラウザ操作（基本）
                - Webページへのアクセス、スクリーンショット撮影
                - 要素のクリック、テキスト入力、フォーム送信
                - JavaScript実行、ページテキスト取得

                ### Webクローラー機能（Firecrawl風）
                - **ScrapeUrl**: URLをスクレイプしてMarkdown形式でコンテンツを取得
                - **CrawlWebsite**: Webサイト全体をクロールして複数ページのコンテンツを取得
                - **MapWebsite**: サイトマップ（全URL一覧）を高速に取得
                - **SearchWeb**: Webを検索して結果を取得
                - **BatchScrape**: 複数のURLを一括スクレイプ
                - **GetPageMarkdown**: 現在のページをMarkdown形式で取得
                - **ExtractLinks**: ページからリンクを抽出
                - **ExtractMetadata**: ページのメタデータを抽出

                ### ブラウザアクション
                - **スクロール**: ページのスクロール、最下部への移動
                - **待機**: 要素表示待機、時間待機、ページ読み込み待機
                - **キーボード**: キー入力（Enter, Tab, Escape など）
                - **ナビゲーション**: 戻る、進む、リロード
                - **その他**: ホバー、ファイルアップロード
                - **ExecuteActions**: 複数のアクションを順番に実行

                ### 高度なブラウザ操作（AdvancedBrowser）
                - **Cookie/ストレージ操作**: Cookie、LocalStorage、SessionStorageの取得・設定
                - **フォーム操作**: セレクトボックス、チェックボックス、ラジオボタン
                - **マウス操作**: ダブルクリック、右クリック、ドラッグ＆ドロップ
                - **テキスト選択**: テキストの選択、コピー、ペースト
                - **iframe操作**: iframe内のコンテンツ取得、要素操作
                - **テキスト検索**: ページ内テキストの検索・ハイライト
                - **テーブル抽出**: テーブルデータの抽出

                ### 構造化データ抽出（DataExtraction）
                - **ExtractStructuredData**: AIを使った構造化データ抽出
                - **ExtractWithSchema**: スキーマベースのデータ抽出
                - **ExtractProductInfo**: 商品情報の抽出
                - **ExtractArticleInfo**: 記事・ニュース情報の抽出
                - **ExtractContactInfo**: 連絡先情報の抽出
                - **SummarizePage**: ページ内容の要約
                - **AnalyzePageContent**: ページ内容の分析・質問応答

                ### 自律エージェント機能（AutonomousAgent）
                - **ExecuteGoalAutonomously**: 目標を設定して自律的に計画・実行
                - **PlanGoal**: 目標の計画だけを作成（実行しない）
                - **PauseExecution/ResumeExecution/CancelExecution**: 実行制御
                - **UpdateSettings/GetSettings**: 自律実行の設定変更
                - **EvaluateLastExecution**: 最後の実行結果を評価

                ### スキルシステム（Skills）
                - **list_skills**: 利用可能なスキル一覧を表示
                - **execute_skill**: 指定したスキルを実行（パラメータはJSON形式）
                - **show_skill**: スキルの詳細情報を表示
                - **register_skill**: 新しいスキルを登録（YAML/JSON形式で定義）
                - **remove_skill**: スキルを削除
                - スキルはプロンプトテンプレートとして定義され、再利用可能です

                ## セキュリティガイドライン
                - 常にセキュリティを最優先してください
                - 許可されていないパスやURLには絶対にアクセスしないでください
                - 危険なコマンドやスクリプトは実行しないでください
                - ユーザーの機密情報を保護してください
                - **重要**: ユーザーが意図しない操作は絶対にしないでください
                - 削除、送信などの危険な操作は必ずユーザーの承認を得てください

                ## 対話スタイル
                - 丁寧で分かりやすい日本語で応答してください
                - タスクの進行状況を適切に報告してください
                - エラーが発生した場合は、原因と解決策を提案してください
                - 不明な点はユーザーに確認してください

                ## 利用可能なプラグイン
                - FileSystem: ファイルシステム操作
                - PowerShell: PowerShellコマンド実行
                - Browser: ブラウザ操作（基本）
                - WebCrawler: Webクローラー・スクレイパー機能
                - BrowserActions: 高度なブラウザアクション
                - AdvancedBrowser: Cookie/ストレージ、フォーム、マウス操作など
                - DataExtraction: AIを使った構造化データ抽出
                - AutonomousAgent: 自律的な目標実行
                - Skills: スキルの管理・実行（プロンプトテンプレート）

                ユーザーのリクエストに対して、安全かつ効率的にタスクを実行してください。
                """,
            Kernel = kernel,
            Arguments = new KernelArguments(
                new OpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                })
        };

        return agent;
    }

    private static async Task RunChatInterfaceAsync(IServiceProvider serviceProvider, ChatCompletionAgent agent)
    {
        var chatInterface = serviceProvider.GetRequiredService<IChatInterface>();
        var kernel = serviceProvider.GetRequiredService<Kernel>();

        Console.WriteLine("チャットを開始します。終了するには 'exit' または 'quit' と入力してください\n");

        // メッセージハンドラーを設定
        async Task<string> HandleMessage(ChatMessageReceivedEventArgs e)
        {
            return await ProcessMessageAsync(agent, e.Content);
        }

        var manager = new ChatInterfaceManager(HandleMessage);
        manager.AddInterface(chatInterface);

        // インターフェースを開始
        await manager.StartAllAsync();

        // CLIモードでは同期的に待機
        while (chatInterface.IsConnected)
        {
            await Task.Delay(100);
        }

        await manager.DisposeAsync();
    }

    private static async Task<string> ProcessMessageAsync(ChatCompletionAgent agent, string userMessage)
    {
        try
        {
            var chat = new AgentGroupChat(agent);

            chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, userMessage));

            var responseBuilder = new System.Text.StringBuilder();

            await foreach (var message in chat.InvokeAsync())
            {
                if (!string.IsNullOrEmpty(message.Content))
                {
                    responseBuilder.AppendLine(message.Content);
                }
            }

            return responseBuilder.ToString().Trim();
        }
        catch (Exception ex)
        {
            return $"エラーが発生しました: {ex.Message}";
        }
    }
}
