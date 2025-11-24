# MpegTsRtpGateway

完全なTSデマックスとRFC 6184準拠RTPパケット化を備えたMPEG-TS→H.264 RTP変換ゲートウェイ

## 概要

MpegTsRtpGatewayは、UDPマルチキャスト経由でMPEG-TSストリームを受信し、H.264映像を抽出して、RFC 6184準拠のH.264 RTPパケットに変換しユニキャスト配信する高度なゲートウェイです。

### 主な機能
- **完全TSデマックス**: PAT/PMT解析によるH.264ストリーム検出
- **PES組み立て**: TSパケットからPESパケットを再構築
- **PTS抽出**: MPEG-TS PTSから正確なRTPタイムスタンプ生成
- **RFC 6184準拠**: Single NALおよびFU-Aフラグメンテーションモード
- **低CPU使用率**: 平均約0.33%（最適化されたパイプライン）
- **クリーンシャットダウン**: 適切なCtrl+C処理

## アーキテクチャ

```
VLC (MPEG-TS over UDPマルチキャスト)
    |
    | UDP 239.0.0.1:5004
    | MPEG-TS (内部にH.264映像)
    v
MpegTsRtpGateway
    |
    ├─> TsDemuxer (PAT/PMT → ビデオPID)
    ├─> PesAssembler (TS → PES + PTS)
    ├─> H264NalParser (PES → NALユニット)
    └─> H264RtpPacker (NAL → RTPパケット)
    |
    | HTTP POST /offer (SDP Offer/Answer)
    | UDP 127.0.0.1:5006 → クライアントポート
    v
RtpClientSipsorcery
```

## 処理パイプライン

```
MPEG-TSパケット (188バイト)
    ↓ TsDemuxer
ビデオPID TSパケット
    ↓ PesAssembler
PESパケット + PTS (90kHz)
    ↓ H264NalParser
NALユニット (Annex B)
    ↓ H264RtpPacker
RTPパケット (RFC 6184)
```

## 使い方

### 1. VLCでMPEG-TSマルチキャストを開始

**Linux/macOS:**
```bash
cvlc screen:// --screen-fps=60 \
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 \
  --sout '#transcode{vcodec=h264,acodec=none}:udp{mux=ts,dst=239.0.0.1:5004}' \
  --sout-keep
```

**Windows PowerShell:**
```powershell
& "C:\Program Files\VideoLAN\VLC\vlc.exe" `
  screen:// --screen-fps=60 `
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 `
  --sout '#transcode{vcodec=h264,acodec=none}:udp{mux=ts,dst=239.0.0.1:5004}' `
  --sout-keep
```

**ポイント:**
- `mux=ts`でH.264をMPEG-TSでラップ
- `--sout-keep`で連続ストリーミング確保

### 2. ゲートウェイを起動

```bash
cd csharp/MpegTsRtpGateway
dotnet run
```

期待される出力:
```
=== MpegTsRtpGateway starting ===
TS Input: udp://239.0.0.1:5004
HTTP Server: http://localhost:8080/offer

HTTP server listening on port 8080
Listening for TS multicast on 239.0.0.1:5004
Gateway is running. Press Ctrl+C to exit.
```

### 3. クライアントを起動

```bash
cd csharp/RtpClientSipsorcery
dotnet run
```

## コンポーネント

### TsPacket
188バイトのMPEG-TSパケットを表現
- TSヘッダー解析（sync byte、PID、continuity counter）
- アダプテーションフィールド処理
- ペイロード抽出

### TsDemuxer
MPEG-TSストリームをデマックスしH.264映像を検出
- PAT（Program Association Table）を解析してPMT PIDを検出
- PMT（Program Map Table）を解析してビデオPIDを検出
- ビデオPIDでTSパケットをフィルタリング
- ストリームタイプ0x1B（H.264/AVC）をサポート

### PesAssembler
TSパケットからPESパケットを組み立て
- PES開始を検出（payload_unit_start_indicator）
- PTS（Presentation Time Stamp）を90kHzで抽出
- 完全なPESペイロード（H.264 ES）を組み立て

### H264NalParser
H.264 Annex B形式を解析
- スタートコード（0x000001または0x00000001）を検出
- NALユニットを抽出
- 全NALタイプ（SPS、PPS、IDR、non-IDR等）をサポート

### H264RtpPacker
H.264 NALユニットをRTP（RFC 6184）にパケット化
- **Single NALモード**: 小さいNALユニット用（≤1200バイト）
- **FU-Aモード**: 大きいNALユニットのフラグメンテーション
- 適切なRTPヘッダー生成（シーケンス、タイムスタンプ、マーカー）
- PTSをRTPタイムスタンプとして使用

## パフォーマンス

### ベンチマーク
- **CPU使用率**: 平均0.33%、ピーク2%
- **メモリ**: 約55-60 MB RSS
- **処理時間**: フレームあたり5ms未満
- **スループット**: 最大20 Mbps（テスト済み）

### パフォーマンス特性
- 可能な限りゼロコピー
- 効率的なバッファ管理
- ホットパスでの最小限のアロケーション
- 最適化されたTS解析

## トラブルシューティング

### ビデオPIDが検出されない

VLC出力形式を確認:
```bash
# 正しい: MPEG-TSでH.264を使用
--sout '#transcode{vcodec=h264}:udp{mux=ts,dst=239.0.0.1:5004}'

# 間違い: 直接RTP（代わりにUdpRtpGatewayを使用）
--sout '#transcode{vcodec=h264}:rtp{dst=239.0.0.1,port=5004}'
```

### Wiresharkで確認

**TS入力 (239.0.0.1:5004):**
```
フィルター: udp.port == 5004
デコード: MPEG-TS
確認: PMTがstream_type = 0x1B (H.264)を表示
```

**RTP出力 (127.0.0.1:5006):**
```
フィルター: udp.port == 5006
プロトコル: RTP
確認: PT=96、適切なシーケンス番号
```

## 技術詳細

詳細な技術情報は[英語版ドキュメント](MpegTsRtpGateway.md)を参照してください。

## 関連項目
- [UdpRtpGateway](UdpRtpGateway_ja.md) - 直接H.264 RTP入力用
- [RFC 6184](https://tools.ietf.org/html/rfc6184) - H.264ビデオのRTPペイロードフォーマット
- [ISO 13818-1](https://www.iso.org/standard/74427.html) - MPEG-TS仕様
- [メインドキュメント](../README_ja.md)
