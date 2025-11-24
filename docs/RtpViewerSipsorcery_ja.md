# RtpViewerSipsorcery

SDP Offer/Answer交換でH.264ビデオストリームを受信し、FIFOパイプを介してffplayで表示するC# RTPビューアアプリケーション。

## 概要

RtpViewerSipsorceryは、以下の機能を持つ軽量なH.264 RTPストリームビューアです：
1. RecvOnlyビデオトラックでRTPセッションを作成
2. ゲートウェイ（デフォルト: `http://127.0.0.1:8080/offer`）にSDP Offerを送信
3. RTPパケットを受信し、H.264 NALユニットをFIFOパイプに書き込み
4. ffplayを使用してビデオストリームを表示

このビューアは、`MpegTsRtpGateway`や`UdpRtpGateway`などのRTPゲートウェイと連携して動作するよう設計されています。

## 機能

- **SDPベースのセッションネゴシエーション**: 自動的なOffer/Answer交換
- **FIFOベースのストリーミング**: 名前付きパイプを使用した効率的なプロセス間通信
- **H.264 NALユニット処理**: Single NAL、STAP-A、FU-Aパケットに対応
- **リアルタイム再生**: ffplayによる低遅延ビデオ表示
- **詳細なログ出力**: シーケンス番号、タイムスタンプ、NALタイプを含むパケット単位のログ
- **適切なエラー処理**: ffplayの終了やパイプの切断時のクリーンアップ

## 要件

- **.NET 8.0 SDK** 以降
- **ffmpeg/ffplay** がインストールされ、PATHに含まれていること
- **Linux/macOS** (FIFOパイプはUnix固有)
- **SIPSorcery** NuGetパッケージ（自動的に復元）

### ffmpegのインストール

**Ubuntu/Debian:**
```bash
sudo apt install ffmpeg
```

**macOS:**
```bash
brew install ffmpeg
```

## ビルド

```bash
cd csharp/RtpViewerSipsorcery
dotnet build -c Release
```

## 使用方法

### 基本的な使用方法

1. ゲートウェイを起動（例：MpegTsRtpGatewayまたはUdpRtpGateway）：
   ```bash
   cd csharp/MpegTsRtpGateway
   dotnet run -c Release
   ```

2. ビューアを起動：
   ```bash
   cd csharp/RtpViewerSipsorcery
   dotnet run -c Release
   ```

3. ビデオストリーミングを開始（[動作確認済みストリーム](#動作確認済みストリーム)を参照）

### 設定

`Program.cs`を編集してカスタマイズ：

- **ゲートウェイURL**（11行目）：
  ```csharp
  const string GATEWAY_HTTP_URL = "http://127.0.0.1:8080/offer";
  ```

- **H.264ペイロードタイプ**（16行目）：
  ```csharp
  const int H264_PAYLOAD_TYPE = 96;
  ```

- **FIFOパス**（`H264StreamWriter`の225行目）：
  ```csharp
  private readonly string _fifoPath = "/tmp/rtp_stream.h264";
  ```

## 動作確認済みストリーム

以下のストリームソースがテスト済みです：

### MPEG-TS over UDP マルチキャスト

#### テストパターン（ffmpeg）
```bash
ffmpeg -f lavfi -i testsrc=size=640x360:rate=30 \
  -c:v libx264 -preset ultrafast -tune zerolatency \
  -g 30 -keyint_min 30 -x264-params "repeat-headers=1:bframes=0" \
  -f mpegts udp://239.0.0.1:5004
```

#### ビデオファイル - トランスコードなし（ffmpeg）
```bash
ffmpeg -re -stream_loop -1 -i video.mp4 \
  -c:v copy -an -f mpegts udp://239.0.0.1:5004
```

#### ビデオファイル - トランスコードなし（cvlc）
```bash
cvlc --loop video.mp4 \
  --sout '#std{access=udp,mux=ts,dst=239.0.0.1:5004}' \
  --sout-keep
```

### RTP マルチキャスト

#### テストパターン（ffmpeg）
```bash
ffmpeg -f lavfi -i testsrc=size=640x360:rate=30 \
  -c:v libx264 -preset ultrafast -tune zerolatency \
  -g 30 -keyint_min 30 -x264-params "repeat-headers=1:bframes=0" \
  -f rtp rtp://239.0.0.1:5004
```

#### ビデオファイル - トランスコードあり（ffmpeg）
```bash
ffmpeg -re -stream_loop -1 -i video.mp4 \
  -c:v libx264 -preset ultrafast -tune zerolatency \
  -g 30 -x264-params "repeat-headers=1" \
  -an -f rtp rtp://239.0.0.1:5004
```

**注意:** RTPマルチキャストの場合、SPS/PPSが定期的に送信されるよう`repeat-headers=1`を使用したトランスコードを推奨します。

## アーキテクチャ

### コンポーネント

```
┌─────────────────────────────────────────────────────────┐
│                  RtpViewerSipsorcery                    │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  ┌──────────────┐  SDP Offer   ┌──────────────────┐   │
│  │  RTPSession  │──────────────>│  HTTP Client     │   │
│  │  (RecvOnly)  │<──────────────│  (Gateway)       │   │
│  └──────┬───────┘  SDP Answer   └──────────────────┘   │
│         │                                               │
│         │ RTP Packets                                   │
│         v                                               │
│  ┌──────────────────────────────────────────────────┐  │
│  │        H264StreamWriter                          │  │
│  │  - FIFO作成: /tmp/rtp_stream.h264               │  │
│  │  - Single NAL、STAP-A、FU-A処理                 │  │
│  │  - スタートコード付きH.264バイトストリーム書込  │  │
│  └──────────────────────────────────────────────────┘  │
│         │                                               │
│         │ FIFOパイプ                                    │
│         v                                               │
│  ┌──────────────────────────────────────────────────┐  │
│  │  ffplayプロセス                                  │  │
│  │  - FIFOから読み込み                              │  │
│  │  - H.264デコード                                 │  │
│  │  - ビデオ表示                                    │  │
│  └──────────────────────────────────────────────────┘  │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### H.264 NALユニット処理

ビューアは3つのRTPパケット化モードに対応：

1. **Single NALユニット**: 1つのRTPパケットに完全なNALユニット
   - 0x00000001スタートコードを付けて直接書き込み

2. **STAP-A（Single-Time Aggregation Packet）**: 1つのRTPパケットに複数のNALユニット
   - 各NALユニットに2バイトのサイズプレフィックス
   - 全NALユニットを抽出して個別に書き込み

3. **FU-A（Fragmentation Unit）**: 大きなNALユニットを複数のRTPパケットに分割
   - 開始、中間、終了フラグメントを再構築
   - 終了フラグメント到着時に完全なNALユニットを書き込み

### FIFO（名前付きパイプ）

アプリケーションが通常ファイルではなくFIFOを使用する理由：
- **ディスク蓄積なし**: データはffplayによって即座に消費される
- **同期**: 書き込み側は読み込み側の準備が整うまでブロック
- **ストリーミング**: リアルタイムビデオに適した真のストリームセマンティクス

**FIFOライフサイクル:**
1. `mkfifo`コマンドでFIFO作成
2. ffplayを起動してFIFOを読み込み用にオープン
3. FIFOを書き込み用にオープン（ffplay接続までブロック）
4. FIFOを通じてH.264データをストリーミング
5. 終了時にFIFOをクリーンアップ

## トラブルシューティング

### ffplayが見つからない

**エラー:**
```
ERROR: ffplay not found in PATH!
```

**解決方法:**
```bash
# Ubuntu/Debian
sudo apt install ffmpeg

# macOS
brew install ffmpeg
```

### Broken pipeエラー

**エラー:**
```
System.IO.IOException: Broken pipe
```

**原因:** ビューアが書き込みを完了する前にffplayが終了した場合に発生します。

**解決方法:** 現在のコードでは適切に処理されており、クリーンアップ時にエラーが捕捉され無視されます。

### ビデオが表示されない

**考えられる原因:**

1. **ゲートウェイが起動していない**: MpegTsRtpGatewayまたはUdpRtpGatewayが先に起動していることを確認
2. **ストリームソースがない**: ffmpeg/cvlcストリームソースを起動
3. **SPS/PPSが欠落**: 信頼性の高い再生には`repeat-headers=1`を使用したトランスコードを使用
4. **ファイアウォールでブロック**: UDPマルチキャストがブロックされていないか確認

### デコーダエラー

**エラー:**
```
[h264 @ ...] non-existing PPS 0 referenced
```

**解決方法:**
- MPEG-TS over UDPを使用（生のRTPより信頼性が高い）
- または`-x264-params "repeat-headers=1"`を使用したトランスコードを使用

## 関連プロジェクト

- **MpegTsRtpGateway**: MPEG-TS over UDPをH.264 RTPユニキャストに変換
- **UdpRtpGateway**: RTPマルチキャストをRTPユニキャストにリレー

## ライセンス

このプロジェクトは[SIPSorcery](https://github.com/sipsorcery-org/sipsorcery)ライブラリを使用しており、BSD 3-Clause Licenseの下でライセンスされています。
