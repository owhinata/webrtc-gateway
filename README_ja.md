# WebRTC Gateway

RTP マルチキャスト→ユニキャスト変換ゲートウェイ（SDP Offer/Answer 対応）

## 概要

このソリューションは、H.264 ビデオストリームを RTP マルチキャストで受信し、ユニキャストでクライアントに中継します。クライアントは HTTP 経由で SDP Offer/Answer を使用してゲートウェイに登録します。

### アーキテクチャ

```
VLC (画面キャプチャ)
    |
    | マルチキャスト RTP (239.0.0.1:5004, H.264)
    v
UdpRtpGateway (ポート 8080)
    |
    | ユニキャスト RTP (ポート 5006 → クライアントポート)
    v
RtpClientSipsorcery
```

## 必要なもの

- .NET 8.0 SDK
- VLC Media Player
- Windows (Windows 11 で動作確認済み)

## プロジェクト

### UdpRtpGateway
- RTP マルチキャストストリームを受信 (239.0.0.1:5004)
- SDP Offer/Answer 交換用 HTTP サーバー (ポート 8080)
- 登録されたクライアントに RTP パケットをユニキャストで中継

### RtpClientSipsorcery
- HTTP 経由でゲートウェイに SDP Offer を送信
- SIPSorcery ライブラリを使用して RTP ストリームを受信
- パケット統計を表示（レート、ビットレート、シーケンス、タイムスタンプ）

### MulticastSender
- マルチキャストパケット送信用のテストユーティリティ
- VLC マルチキャストストリームをシミュレート

## ビルド

```bash
# ゲートウェイをビルド
cd csharp/UdpRtpGateway
dotnet build

# クライアントをビルド
cd csharp/RtpClientSipsorcery
dotnet build
```

## 使い方

### 1. VLC で H.264 RTP マルチキャストを開始

**PowerShell:**
```powershell
& "C:\Program Files\VideoLAN\VLC\vlc.exe" `
  screen:// `
  --screen-fps=60 `
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 `
  --sout '#transcode{vcodec=h264,acodec=none}:rtp{dst=239.0.0.1,port=5004,proto=udp}' `
  --sout-keep
```

**重要:** Payload Type 96 に合わせるため `vcodec=h264` を使用してください（TS ではない）。

### 2. ゲートウェイを起動

```bash
cd csharp/UdpRtpGateway
dotnet run
```

期待される出力:
```
UDP RTP Gateway starting...
Multicast: 239.0.0.1:5004
HTTP Server: http://+:8080/
Joined multicast group 239.0.0.1:5004
Send client bound to port 5006
```

### 3. クライアントを起動

```bash
cd csharp/RtpClientSipsorcery
dotnet run
```

期待される出力:
```
[FIRST RTP PACKET] From=127.0.0.1:5006, Media=video, PT=96, Seq=11189, TS=1271181839, Len=11
Total: 23 packets | Rate: 21.8 pps | Bitrate: 123.4 kbps | Last Seq: 11211 | Last TS: 1271204367
```

## 主な設定

### ゲートウェイ
- マルチキャスト受信: `239.0.0.1:5004`
- HTTP サーバー: `http://+:8080/offer`
- RTP 送信ポート: `5006` (SDP Answer と一致)
- Payload Type: `96` (H.264)

### クライアント
- ゲートウェイ URL: `http://127.0.0.1:8080/offer`
- Payload Type: `96` (H.264)
- ストリーム方向: RecvOnly

## トラブルシューティング

### ポート 8080 アクセス拒否

管理者として実行するか、URL 予約を設定してください:
```powershell
netsh http add urlacl url=http://+:8080/ user=Everyone
```

### パケットが受信できない

**Payload Type の不一致を確認:**
- VLC は H.264 RTP を使用する必要があります (`vcodec=h264`)
- MPEG-TS を使用しないでください (`Video - H264 + MP3 (TS)`)
- クライアントは PT=96 (H.264) を期待しており、PT=33 (MPEG-TS) ではありません

**Wireshark で確認:**
- ゲートウェイからの RTP パケット (127.0.0.1:5006 → 127.0.0.1:client_port)
- RTP ヘッダーを確認: `0x80 0x60` (PT=96 for H.264)

### リスニングポートの確認 (PowerShell)

```powershell
Get-NetUDPEndpoint | Where-Object {$_.OwningProcess -in (Get-Process dotnet).Id}
```

## 技術詳細

### SDP 交換
1. クライアントが SDP Offer を生成 (RecvOnly, PT=96)
2. クライアントが HTTP POST で Offer をゲートウェイに送信
3. ゲートウェイが SDP Answer で応答 (SendOnly, PT=96)
4. クライアントが Answer を適用し、RTP セッションを開始

### RTP 中継
- ゲートウェイは送信ソケットをポート 5006 にバインド (SDP Answer と一致)
- ゲートウェイは 127.0.0.1:5006 からクライアントのポートに送信
- クライアントは 127.0.0.1:5006 からの RTP を受け入れ (SDP 検証)

## ライセンス

このサンプル実装は教育目的です。
