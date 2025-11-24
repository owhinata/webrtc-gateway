# RTP マルチキャストゲートウェイ - Python実装

SDPベースのRTPマルチキャストストリーミングシステムのPython実装。

## 機能

- **SDPサーバー (`sdp_server.py`)**: SDP Offer/Answer交換を通じてマルチキャストストリーム情報を提供
- **RTPマルチキャストクライアント (`rtp_multicast_client.py`)**: SDPネゴシエーション後にRTPマルチキャストストリームを受信

## アーキテクチャ

```
VLC → マルチキャスト (239.0.0.1:5004)
         ↑
         └─ クライアント（マルチキャストに直接参加）
              ↑
              └─ SDP交換でマルチキャスト情報を取得 (sdp_server)
```

このシステムはSDP (Session Description Protocol)を使用して、クライアントにマルチキャストストリームの場所を通知します。クライアントはマルチキャストグループに直接参加します - サーバーによるRTPパケットの中継は行われません。

## 必要要件

- Python 3.8以上
- `requirements.txt`に記載された依存ライブラリ

## インストール

依存ライブラリをインストール:

```bash
pip install -r requirements.txt
```

または仮想環境を使用（推奨）:

```bash
python3 -m venv venv
source venv/bin/activate  # Windows: venv\Scripts\activate
pip install -r requirements.txt
```

## 使い方

### 1. VLCでマルチキャスト配信を開始

```bash
vlc your-video.mp4 --sout '#rtp{dst=239.0.0.1,port=5004,mux=ts}' --loop
```

これにより、ビデオがマルチキャストアドレス `239.0.0.1:5004` にストリーミングされます。

### 2. SDPサーバーを起動

```bash
python sdp_server.py
```

サーバーは以下を行います:
- ポート `8080` でHTTPサーバーを起動（SDPネゴシエーション用）
- クライアントにマルチキャストストリーム情報 (`239.0.0.1:5004`) を提供

### 3. クライアントを起動

```bash
python rtp_multicast_client.py
```

クライアントは以下を行います:
- `http://127.0.0.1:8080/offer` にSDP Offerを送信
- マルチキャスト情報を含むSDP Answerを受信
- マルチキャストグループに参加
- RTPパケットの受信と統計情報の表示を開始

## 設定

ソースファイル内の以下の定数を変更できます:

### sdp_server.py

- `MULTICAST_ADDRESS`: RTPストリームのマルチキャストアドレス (デフォルト: `239.0.0.1`)
- `MULTICAST_PORT`: マルチキャストポート (デフォルト: `5004`)
- `HTTP_PORT`: SDPネゴシエーション用HTTPサーバーポート (デフォルト: `8080`)
- `H264_PAYLOAD_TYPE`: H.264ペイロードタイプ (デフォルト: `96`)

### rtp_multicast_client.py

- `GATEWAY_HTTP_URL`: SDPサーバーURL (デフォルト: `http://127.0.0.1:8080/offer`)
- `MULTICAST_ADDRESS`: デフォルトマルチキャストアドレス (デフォルト: `239.0.0.1`)
- `MULTICAST_PORT`: デフォルトマルチキャストポート (デフォルト: `5004`)
- `H264_PAYLOAD_TYPE`: H.264ペイロードタイプ (デフォルト: `96`)

## 開発

### コードフォーマット

blackでコードをフォーマット:

```bash
black sdp_server.py rtp_multicast_client.py
```

### コードリント

flake8を実行:

```bash
flake8 sdp_server.py rtp_multicast_client.py
```

pylintを実行:

```bash
pylint sdp_server.py rtp_multicast_client.py
```

mypyを実行:

```bash
mypy sdp_server.py rtp_multicast_client.py
```

### インポートソート

isortでインポートをソート:

```bash
isort sdp_server.py rtp_multicast_client.py
```

### すべてのチェックを実行

```bash
black sdp_server.py rtp_multicast_client.py && \
isort sdp_server.py rtp_multicast_client.py && \
flake8 sdp_server.py rtp_multicast_client.py && \
pylint sdp_server.py rtp_multicast_client.py && \
mypy sdp_server.py rtp_multicast_client.py
```

## 動作原理

### SDPサーバー

1. `/offer` でHTTP POSTリクエストを待機
2. クライアントからSDP Offerを受信
3. マルチキャストストリーム情報を含むSDP Answerを返却
4. RTPパケットの中継は行わない

### クライアント

1. デフォルトのマルチキャスト情報でSDP Offerを作成
2. HTTP POSTでSDPサーバーにOfferを送信
3. 実際のマルチキャストアドレス/ポートを含むSDP Answerを受信
4. Answerを解析して接続詳細を抽出
5. UDPソケットを作成してマルチキャストグループに参加
6. RTPパケットを受信して統計情報を表示

## C#実装との比較

このPython実装はC#版と同様の機能を提供しますが、簡素化されたアーキテクチャを採用しています:

- **C# UdpRtpGateway** → **Python sdp_server.py** (簡素化、RTP中継なし)
- **C# RtpClientSipsorcery** → **Python rtp_multicast_client.py**

主な違い:
- RTPパケット中継なし - クライアントが直接マルチキャストに参加
- WebRTC機能を持たないシンプルなSDP交換
- HTTPサーバー/クライアントに `aiohttp` を使用
- `asyncio` を使った非同期/await パターン
- マルチキャストUDPにPython組み込みの `socket` モジュールを使用

## トラブルシューティング

### サーバーが接続を受け付けない

- ファイアウォール設定を確認
- ポート8080が利用可能か確認
- サーバーが `0.0.0.0` でリスニングしているか確認

### クライアントがマルチキャストパケットを受信しない

- マルチキャストトラフィック用のファイアウォール設定を確認
- ネットワークでマルチキャストルーティングが有効か確認
- VLCが正しいアドレス/ポートにストリーミングしているか確認
- SDPネゴシエーションが正常に完了しているか確認

### VLCストリーミングの問題

- ビデオファイルのパスが正しいか確認
- VLCがインストールされており、コマンドラインからアクセス可能か確認
- マルチキャストアドレスが有効範囲内 (224.0.0.0 - 239.255.255.255) か確認

## ライセンス

親プロジェクトと同じ。
