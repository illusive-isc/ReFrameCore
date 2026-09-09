using jp.illusive_isc.ReFrame.Core;

namespace jp.illusive_isc.ReFrame.IKUSIA
{
    /// <summary>IKUSIA 系アバター (kaguya / rurune …) に共通する行。</summary>

    [ReFrameDeleteWhenAllGone("Action_Mode", "paryi_chang_Loco", "takasa_Toggle", "Mirror")]
    [ReFrameDeleteWhenAllGone("Action_Mode_Reset", "paryi_chang_Loco", "takasa_Toggle")]
    [ReFrameDeleteWhenAllGone(
        "paryi_change_all_reset",
        "paryi_change_Standing",
        "paryi_change_Crouching",
        "paryi_change_Prone",
        "paryi_floating"
    )]
    [ReFrameGroupLabel("Gimmick", "ギミック")]
    [ReFrameGroupLabel("IKUSIA_emote", "エモート・姿勢")]
    [ReFrameGroupLabel("Face", "表情")]
    [ReFrameGroupLabel("VRCEmote", "エモート")]
    [ReFrameGroupLabel("standing", "立ち")]
    [ReFrameGroupLabel("crouching", "しゃがみ")]
    [ReFrameGroupLabel("prone", "伏せ")]
    [ReFrameGroupLabel("floating", "浮遊")]
    [ReFrameGroupLabel("IKUSIA_Loco", "ロコモーション (IKUSIA_Loco)")]
    [ReFrameGroupLabel("Particle", "パーティクル")]
    public abstract class IKUSIACommonReFrame : ReFrameIKUSIA
    {

        [ReFrameMenuGroup("Gimmick", "Face")]
        [ReFrameDelete("FaceLock", ReFrameParameterType.Bool)]
        [ReFrameDeleteRelatedBlendTree("FaceLock", Always = true)]
        [ReFrameValueLocked]
        [ReFrameLabel("表情ロック")]
        public ReFrameDeleteEntry faceLock = new() { Enabled = false, Value = 0f };
        [ReFrameMenuGroup("Gimmick", "Face")]
        [ReFrameDelete("Nade", ReFrameParameterType.Bool)]
        [ReFrameDeleteObject("Advanced/Gimmick2/Face2", 0f)]
        [ReFrameCutUndrivenTransitions("HeadContactNadeNade", EvaluateAsFixed = true)]
        [ReFrameCutUndrivenTransitions("shoulderContactL", EvaluateAsFixed = true)]
        [ReFrameCutUndrivenTransitions("shoulderContactR", EvaluateAsFixed = true)]
        [ReFrameLabel("なでなで (頭・肩の反応)")]
        public ReFrameDeleteEntry nade = new() { Enabled = false, Value = 1f };
        [ReFrameMenuGroup("Gimmick", "Face")]
        [ReFrameDelete("Kamituki", ReFrameParameterType.Bool)]
        [ReFrameCutUndrivenTransitions("biteContact", EvaluateAsFixed = true)]
        [ReFrameLabel("噛みつき")]
        public ReFrameDeleteEntry kamituki = new() { Enabled = false, Value = 0f };
        [ReFrameMenuGroup("Gimmick", "Face")]
        [ReFrameDelete("Face_variation")]
        [ReFrameLabel("表情差分")]
        public ReFrameDeleteEntry faceVariation = new() { Enabled = false, Value = 0f };

        [ReFrameApplyToAvatar]
        [ReFrameDelete("BreastSize", ReFrameParameterType.Float)]
        [ReFrameLabel("胸サイズ")]
        public ReFrameDeleteEntry breastSize = new() { Enabled = false, Value = 0f };

        [ReFrameMenuGroup("IKUSIA_emote", "姿勢変更", "VRCEmote")]
        [ReFrameValueLocked(0f)]
        [ReFrameDelete("VRCEmote", ReFrameParameterType.Int)]
        [ReFrameLabel("エモート (VRCEmote)")]
        public ReFrameDeleteEntry vrcEmote = new() { Enabled = false, Value = 0f };

        [ReFrameBundleMember("VRCEmote")]
        [ReFrameDelete("EmoteMirror", ReFrameParameterType.Bool)]
        [ReFrameLabel("エモートの左右反転")]
        public ReFrameDeleteEntry emoteMirror = new() { Enabled = false, Value = 0f };
        [ReFrameDelete("paryi_AFK", ReFrameParameterType.Bool)]
        [ReFrameLabel("AFK")]
        public ReFrameDeleteEntry afk = new() { Enabled = false, Value = 0f };
        [ReFrameDelete("paryi_Jump_cancel", ReFrameParameterType.Bool)]
        [ReFrameLabel("ジャンプモーション OFF")]
        public ReFrameDeleteEntry jumpCancel = new() { Enabled = false, Value = 1f };

        [ReFrameLabel("視点の合わせ直し (ロード時)")]
        [ReFrameValueLocked(0f)]
        [ReFrameDelete("Mirror Toggle", ReFrameParameterType.Bool)]
        [ReFrameDelete("paryi_Jump", ReFrameParameterType.Bool)]
        public ReFrameDeleteEntry viewpointOneShot = new() { Enabled = true, Value = 0f };

        [ReFrameMenuGroup("IKUSIA_emote", "姿勢変更")]
        [ReFrameDelete("leg fixed", ReFrameParameterType.Bool)]
        [ReFrameLabel("足固定")]
        public ReFrameDeleteEntry legFixed = new() { Enabled = false, Value = 0f };

        [ReFrameDelete("paryi_change_Standing", ReFrameParameterType.Float)]
        [ReFrameLabel("立ちポーズ")]
        public ReFrameDeleteEntry standingPoses = new() { Enabled = false, Value = 0f };

        [ReFrameBundleMember("paryi_change_Standing", ValueMatters = true)]
        [ReFrameDelete("paryi_change_Mirror_S", ReFrameParameterType.Bool)]
        [ReFrameLabel("立ちの左右反転")]
        public ReFrameDeleteEntry standingMirror = new() { Enabled = false, Value = 0f };

        [ReFrameDelete("paryi_change_Crouching", ReFrameParameterType.Float)]
        [ReFrameLabel("しゃがみポーズ")]
        public ReFrameDeleteEntry crouchingPoses = new() { Enabled = false, Value = 0f };

        [ReFrameBundleMember("paryi_change_Crouching", ValueMatters = true)]
        [ReFrameDelete("paryi_change_Mirror_C", ReFrameParameterType.Bool)]
        [ReFrameLabel("しゃがみの左右反転")]
        public ReFrameDeleteEntry crouchingMirror = new() { Enabled = false, Value = 0f };

        [ReFrameDelete("paryi_change_Prone", ReFrameParameterType.Float)]
        [ReFrameLabel("伏せポーズ")]
        public ReFrameDeleteEntry pronePoses = new() { Enabled = false, Value = 0f };

        [ReFrameBundleMember("paryi_change_Prone", ValueMatters = true)]
        [ReFrameDelete("paryi_change_Mirror_P", ReFrameParameterType.Bool)]
        [ReFrameLabel("伏せの左右反転")]
        public ReFrameDeleteEntry proneMirror = new() { Enabled = false, Value = 0f };

        [ReFrameDelete("paryi_floating", ReFrameParameterType.Float)]
        [ReFrameLabel("浮遊ポーズ")]
        public ReFrameDeleteEntry floatingPoses = new() { Enabled = false, Value = 0f };

        [ReFrameMenuGroup("IKUSIA_emote", "姿勢変更", "floating")]
        [ReFrameBundleMember("paryi_floating", ValueMatters = true)]
        [ReFrameDelete("paryi_change_Mirror_H", ReFrameParameterType.Bool)]
        [ReFrameLabel("浮遊の左右反転")]
        public ReFrameDeleteEntry floatingMirror = new() { Enabled = false, Value = 0f };

        [ReFrameLabel("ロコモーション (ポーズ・身長)")]
        [ReFrameMenuGroup("IKUSIA_emote", "IKUSIA_Loco")]
        [ReFrameValueLocked(0f)]
        [ReFrameDeleteLayer("pose", 0f)]
        [ReFrameDeleteLayer("takasa", 0f)]
        [ReFrameDeleteLayer("takasalimit", 0f)]
        [ReFrameDelete("paryi_chang_Loco", ReFrameParameterType.Float)]
        [ReFrameDelete("Mirror", ReFrameParameterType.Bool)]
        [ReFrameDelete("takasa_Toggle", ReFrameParameterType.Bool)]
        public ReFrameDeleteEntry ikusiaLoco = new() { Enabled = false, Value = 0f };
    }
}
