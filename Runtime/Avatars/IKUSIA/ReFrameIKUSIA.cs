using jp.illusive_isc.ReFrame.Core;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.IKUSIA
{
    /// <summary>IKUSIA 系アバター共通の ReFrame 基底コンポーネント。</summary>
    [AddComponentMenu("")]
    [ReFramePromo(
        "作者ショップ Illusory Override の商品紹介",
        "https://illusive-isc.booth.pm/",
        ButtonLabel = "BOOTH を開く",
        Description = "ReFrame の作者が BOOTH で配布しているギミック・ツールです (ReFrame の設定とは関係ありません)。"
    )]
    [ReFramePromoItem(
        "もちまるライド",
        "https://illusive-isc.booth.pm/items/8784085",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/MochimaruRide.jpg"
    )]
    [ReFramePromoItem(
        "【輝夜専用】尻尾の抱擁-TailEmbrace-",
        "https://illusive-isc.booth.pm/items/8754642",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/TailEmbrace.jpg"
    )]
    [ReFramePromoItem(
        "【輝夜】燐狐尾",
        "https://illusive-isc.booth.pm/items/8615298",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/Rinkobi.jpg"
    )]
    [ReFramePromoItem(
        "【51アバター対応】舌ピアス -Cat Ver.-（MA設定済み＆セットアップツール付）",
        "https://illusive-isc.booth.pm/items/8572863",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/TonguePiercingCat.jpg"
    )]
    [ReFramePromoItem(
        "一ノ瀬-ichinose用もちふぃった～プロファイル",
        "https://illusive-isc.booth.pm/items/8398769",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/IchinoseMochiFitter.jpg"
    )]
    [ReFramePromoItem(
        "投げナイフ化ツール【真央専用】",
        "https://illusive-isc.booth.pm/items/8358047",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/ThrowingKnifeMao.jpg"
    )]
    [ReFramePromoItem(
        "【３Dモデラー向け衣装セットアップ支援ツール】OutfitWorkbench",
        "https://illusive-isc.booth.pm/items/8132411",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/OutfitWorkbench.jpg"
    )]
    [ReFramePromoItem(
        "逃げられない一杯。 Take The Shot【お酒口移しギミック】",
        "https://illusive-isc.booth.pm/items/8071554",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/TakeTheShot.jpg"
    )]
    [ReFramePromoItem(
        "ちょこでぃっぷ",
        "https://illusive-isc.booth.pm/items/7980332",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/ChocoDip.jpg"
    )]
    [ReFramePromoItem(
        "JustSyncBreath 吐息パーティクルギミック",
        "https://illusive-isc.booth.pm/items/7722385",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/JustSyncBreath.jpg"
    )]
    [ReFramePromoItem(
        "【47アバター対応】口ピアス（MA設定済み＆セットアップツール付）",
        "https://illusive-isc.booth.pm/items/7524036",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/LipPiercing.jpg"
    )]
    [ReFramePromoItem(
        "【51アバター対応】舌ピアス（MA設定済み＆セットアップツール付）",
        "https://illusive-isc.booth.pm/items/6936346",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/TonguePiercing.jpg"
    )]
    [ReFramePromoItem(
        "PhysBoneMoveAssistant",
        "https://illusive-isc.booth.pm/items/6781709",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/PhysBoneMoveAssistant.jpg"
    )]
    [ReFramePromoItem(
        "ハートのヘイロー",
        "https://illusive-isc.booth.pm/items/6593729",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/HeartHalo.jpg"
    )]
    [ReFramePromoItem(
        "DissolveChangeAssistant",
        "https://illusive-isc.booth.pm/items/6501132",
        ImagePath = "Packages/jp.illusive-isc.reframe-core/Editor/UI/Promo/DissolveChangeAssistant.jpg"
    )]
    public abstract class ReFrameIKUSIA : ReFrameDeleteComponent { }
}
