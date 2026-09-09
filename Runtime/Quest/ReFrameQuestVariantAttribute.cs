using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent の派生クラスに付けて「Quest 簡易対応版」にする。</summary>
    /// <code>
    /// [ReFrameQuestVariant]
    /// [AddComponentMenu("ILLUSORY OVERRIDE/ReFrame/KaguyaReFrame (Quest 簡易対応版)")]
    /// [ReFrameTheme("…")] // Theme は継承されないので付け直す
    /// public class KaguyaReFrameQuest : KaguyaReFrame { }
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class ReFrameQuestVariantAttribute : Attribute { }
}
