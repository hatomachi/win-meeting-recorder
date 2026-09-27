using System.Text.Json;

var allArgs = string.Join(" ", args);

if (allArgs.Contains("--version"))
{
    Console.WriteLine("GitHub Copilot CLI v0.1.24 (mock-verified)");
    return 0;
}

bool isResume = allArgs.Contains("--resume");

var md = """
# 📋 会議議事録 (AI自動生成)

## 1. 全体サマリー
本会議では、新機能のアーキテクチャおよび社内GitLab連携方針について合意が形成されました。スライド図に基づき、オフラインファーストでの一時保存とWhisper文字起こし、AIによる自動議事録生成のフローが承認されました。

## 2. アジェンダ別 議論内容 ＆ 決定事項
- **画面キャプチャと音声同期**:
  - GDI BitBltとNAudioによる合成録音を単一exeで完結させる方針を確認。
  - 決定事項: 外部常駐プロセス不要のローカルCLI直接実行方式を正式採用。
- **GitLab連携とセキュリティ**:
  - Fine-grained PATの権限とCommits APIの安全性を検証。
  - 決定事項: Repository Read/Write権限でアトミック送信を保証。

## 3. アクションアイテム / ToDo 一覧
| No | タスク内容 | 担当者 | 期限 |
|:---:|:---|:---:|:---:|
| 1 | プロンプト外部調整機能の実機テスト | 担当A | 9/28 |
| 2 | 社内環境への展開 | 担当B | 9/30 |
""";

if (isResume)
{
    md += "\n\n> 💡 **チャット修正反映**: ユーザーからの修正依頼に基づき、アクションアイテムの担当者と期限を具体化しました。";
}

var resultObj = new
{
    messages = new[]
    {
        new
        {
            role = "assistant",
            content = md
        }
    }
};

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine(JsonSerializer.Serialize(resultObj));
return 0;
