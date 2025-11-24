# UdpRtpGateway

SDP Offer/Answerシグナリング対応のRTPマルチキャスト→ユニキャスト直接中継ゲートウェイ

## 概要

UdpRtpGatewayは、マルチキャストグループからH.264 RTPパケットを受信し、登録されたクライアントにユニキャストで中継するシンプルで効率的なゲートウェイです。最小限の処理で直接中継を行います。

### 主な機能
- **直接中継**: トランスコードや再エンコード不要
- **低レイテンシ**: 処理オーバーヘッド最小
- **SDP Offer/Answer**: HTTP経由の標準シグナリング
- **マルチクライアント対応**: 複数クライアントの同時接続可能
- **クリーンシャットダウン**: 適切なCtrl+C処理

## アーキテクチャ

```
VLC (H.264 RTPマルチキャスト)
    |
    | UDP 239.0.0.1:5004
    | RTP PT=96 (H.264)
    v
UdpRtpGateway
    |
    | HTTP POST /offer (SDP Offer/Answer)
    | UDP 127.0.0.1:5006 → クライアントポート
    v
RtpClientSipsorcery
```

## 使い方

### 1. VLCでH.264 RTPマルチキャストを開始

**Linux/macOS:**
```bash
cvlc screen:// --screen-fps=60 \
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 \
  --sout '#transcode{vcodec=h264,acodec=none}:rtp{dst=239.0.0.1,port=5004,proto=udp}' \
  --sout-keep
```

**Windows PowerShell:**
```powershell
& "C:\Program Files\VideoLAN\VLC\vlc.exe" `
  screen:// --screen-fps=60 `
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 `
  --sout '#transcode{vcodec=h264,acodec=none}:rtp{dst=239.0.0.1,port=5004,proto=udp}' `
  --sout-keep
```

**重要:** RTPペイロードタイプ96のため`vcodec=h264`を使用し、MPEG-TSは使用しないでください。

### 2. ゲートウェイを起動

```bash
cd csharp/UdpRtpGateway
dotnet run
```

### 3. クライアントを起動

```bash
cd csharp/RtpClientSipsorcery
dotnet run
```

## パフォーマンス

- **CPU使用率**: 平均0.5%未満
- **メモリ**: 約40-50 MB
- **レイテンシ**: 5ms未満
- **スループット**: 最大20 Mbps（テスト済み）

詳細は[英語版ドキュメント](UdpRtpGateway.md)を参照してください。
