#!/usr/bin/env python3
"""
OpenAI Whisper API 互換モックサーバー
POST /v1/audio/transcriptions (response_format: verbose_json) をエミュレートします。
外部ライブラリ不要（Python 3 標準ライブラリのみ）。
"""

import http.server
import json
import socketserver
import sys
import time

PORT = 8000

class WhisperMockHandler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/health" or self.path == "/":
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(json.dumps({"status": "ok", "service": "whisper-mock"}).encode("utf-8"))
            return
        self.send_response(404)
        self.end_headers()

    def do_POST(self):
        if "/v1/audio/transcriptions" in self.path:
            content_length = int(self.headers.get("Content-Length", 0))
            body = self.rfile.read(content_length)
            
            print(f"[{time.strftime('%X')}] 🎙️ Whisper API 呼び出しを受信: {content_length} bytes")

            # OpenAI Whisper API 互換 verbose_json レスポンス
            mock_response = {
                "task": "transcribe",
                "language": "japanese",
                "duration": 45.0,
                "text": "お疲れ様です。本日の定例会議を始めます。まず画面共有をご確認ください。スライドの進捗について説明します。以上で本日の報告を終わります。",
                "segments": [
                    {
                        "id": 0,
                        "seek": 0,
                        "start": 0.0,
                        "end": 5.2,
                        "text": "お疲れ様です。本日の定例会議を始めます。",
                        "tokens": [50364, 1234, 50624],
                        "temperature": 0.0,
                        "avg_logprob": -0.21,
                        "compression_ratio": 1.1,
                        "no_speech_prob": 0.01
                    },
                    {
                        "id": 1,
                        "seek": 520,
                        "start": 5.5,
                        "end": 12.0,
                        "text": "まず画面共有をご確認ください。",
                        "tokens": [50639, 5678, 50964],
                        "temperature": 0.0,
                        "avg_logprob": -0.18,
                        "compression_ratio": 1.2,
                        "no_speech_prob": 0.02
                    },
                    {
                        "id": 2,
                        "seek": 1200,
                        "start": 12.5,
                        "end": 28.0,
                        "text": "スライドの進捗について説明します。",
                        "tokens": [50989, 9012, 51764],
                        "temperature": 0.0,
                        "avg_logprob": -0.15,
                        "compression_ratio": 1.3,
                        "no_speech_prob": 0.01
                    },
                    {
                        "id": 3,
                        "seek": 2800,
                        "start": 28.5,
                        "end": 44.5,
                        "text": "以上で本日の報告を終わります。",
                        "tokens": [51789, 3456, 52589],
                        "temperature": 0.0,
                        "avg_logprob": -0.19,
                        "compression_ratio": 1.1,
                        "no_speech_prob": 0.03
                    }
                ]
            }

            resp_bytes = json.dumps(mock_response, ensure_ascii=False, indent=2).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(resp_bytes)))
            self.end_headers()
            self.wfile.write(resp_bytes)
            print(f"[{time.strftime('%X')}] ✅ verbose_json レスポンス返却完了 ({len(mock_response['segments'])} セグメント)")
            return

        self.send_response(404)
        self.end_headers()

def run():
    with socketserver.TCPServer(("0.0.0.0", PORT), WhisperMockHandler) as httpd:
        print(f"==================================================")
        print(f" 🎙️ OpenAI Whisper API 互換モックサーバー起動")
        print(f" エンドポイント: http://localhost:{PORT}/v1/audio/transcriptions")
        print(f" ヘルスチェック: http://localhost:{PORT}/health")
        print(f"==================================================")
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nサーバーを停止しました。")

if __name__ == "__main__":
    run()
