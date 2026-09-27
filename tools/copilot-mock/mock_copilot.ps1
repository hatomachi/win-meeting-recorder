param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ArgsList
)

$argsStr = $ArgsList -join " "

if ($argsStr -match "--version") {
    Write-Output "GitHub Copilot CLI v0.1.24 (mock-verified)"
    exit 0
}

$isResume = $argsStr -match "--resume"

$md = "# 📋 会議議事録 (AI自動生成)`n`n" +
"## 1. 全体サマリー`n" +
"本会議では、新機能のアーキテクチャおよび社内GitLab連携方針について合意が形成されました。スライド図に基づき、オフラインファーストでの一時保存とWhisper文字起こし、AIによる自動議事録生成のフローが承認されました。`n`n" +
"## 2. アジェンダ別 議論内容 ＆ 決定事項`n" +
"- **画面キャプチャと音声同期**:`n" +
"  - GDI BitBltとNAudioによる合成録音を単一exeで完結させる方針を確認。`n" +
"  - 決定事項: 外部常駐プロセス不要のローカルCLI直接実行方式を正式採用。`n" +
"- **GitLab連携とセキュリティ**:`n" +
"  - Fine-grained PATの権限とCommits APIの安全性を検証。`n" +
"  - 決定事項: Repository Read/Write権限でアトミック送信を保証。`n`n" +
"## 3. アクションアイテム / ToDo 一覧`n" +
"| No | タスク内容 | 担当者 | 期限 |`n" +
"|:---:|:---|:---:|:---:|`n" +
"| 1 | プロンプト外部調整機能の実機テスト | 担当A | 9/28 |`n" +
"| 2 | 社内環境への展開 | 担当B | 9/30 |"

if ($isResume) {
    $md += "`n`n> 💡 **チャット修正反映**: ユーザーからの修正依頼に基づき、アクションアイテムの担当者と期限を具体化しました。"
}

$resultObj = @{
    messages = @(
        @{
            role = "assistant"
            content = $md
        }
    )
}

$json = $resultObj | ConvertTo-Json -Depth 5
Write-Output $json
exit 0
