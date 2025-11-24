# WebRTC Gateway

マルチキャストストリームをユニキャストに変換する高性能RTPゲートウェイコレクション（SDP Offer/Answerシグナリング対応）

## プロジェクト

### [UdpRtpGateway](docs/UdpRtpGateway_ja.md)
RTPマルチキャスト→ユニキャスト直接中継ゲートウェイ
- **入力**: H.264 RTPマルチキャスト (239.0.0.1:5004)
- **出力**: H.264 RTPユニキャスト (port 5006)
- **用途**: 事前エンコードされたH.264 RTPストリームの単純な中継

### [MpegTsRtpGateway](docs/MpegTsRtpGateway_ja.md)
MPEG-TS → H.264 RTP変換ゲートウェイ
- **入力**: MPEG-TS over UDPマルチキャスト (239.0.0.1:5004)
- **出力**: H.264 RTPユニキャスト (port 5006)
- **用途**: MPEG-TSストリーム（例: 放送）をRTPに変換
- **機能**: TSデマックス、PES組み立て、H.264パース、RFC 6184 RTPパケット化

### [RtpViewerSipsorcery](docs/RtpViewerSipsorcery_ja.md)
ffplay統合のSIPSorceryベースRTPビューア
- **入力**: ゲートウェイからのH.264 RTPユニキャスト (port 5006)
- **出力**: ffplayによるライブビデオ表示
- **機能**: SDP Offer/Answer交換、FIFOベースストリーミング、NALユニット処理

### RtpClientSipsorcery
ゲートウェイテスト用のSIPSorceryベースRTPクライアント
- HTTP経由でSDP Offerを送信
- RTPストリームを受信
- パケット統計を表示

### MulticastSender
マルチキャストパケット送信用テストユーティリティ

## アーキテクチャ

```
映像ソース (VLC/ffmpeg)
    |
    | UDPマルチキャスト
    v
ゲートウェイ (UdpRtpGateway または MpegTsRtpGateway)
    |
    | HTTP: SDP Offer/Answer
    | UDP: RTPユニキャスト
    v
RTPクライアント (RtpClientSipsorcery)
```

## クイックスタート

### 必要なもの
- .NET 8.0 SDK
- VLC Media Player または ffmpeg
- Linux/Windows/macOS

### 1. ゲートウェイを選択

**H.264 RTP入力の場合:**
```bash
cd csharp/UdpRtpGateway
dotnet run
```

**MPEG-TS入力の場合:**
```bash
cd csharp/MpegTsRtpGateway
dotnet run
```

### 2. クライアントを起動
```bash
cd csharp/RtpClientSipsorcery
dotnet run
```

### 3. 映像ストリームを送信

**H.264 RTP (UdpRtpGateway用):**
```bash
cvlc screen:// --screen-fps=60 \
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 \
  --sout '#transcode{vcodec=h264,acodec=none}:rtp{dst=239.0.0.1,port=5004,proto=udp}' \
  --sout-keep
```

**MPEG-TS (MpegTsRtpGateway用):**
```bash
cvlc screen:// --screen-fps=60 \
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 \
  --sout '#transcode{vcodec=h264,acodec=none}:udp{mux=ts,dst=239.0.0.1:5004}' \
  --sout-keep
```

## ドキュメント

- [UdpRtpGateway ドキュメント](docs/UdpRtpGateway_ja.md)
- [MpegTsRtpGateway ドキュメント](docs/MpegTsRtpGateway_ja.md)
- [RtpViewerSipsorcery ドキュメント](docs/RtpViewerSipsorcery_ja.md)

## パフォーマンス

両ゲートウェイは高度に最適化されています:
- **CPU使用率**: 平均1%未満
- **メモリ**: 約50-60 MB
- **レイテンシ**: 最小（<10ms）

## ライセンス

このサンプル実装は教育目的です。
