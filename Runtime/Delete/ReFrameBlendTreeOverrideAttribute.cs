using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。</summary>
    /// <code>
    /// BT[kaguya bag]  param = kaguya sailor
    /// th=0 (セーラーOFF) -> BT[kaguya bag] param = kaguya outer
    /// th=0 (アウターOFF) -> clip: kaguya bag OFF   ← 強制 OFF
    /// th=1 (アウターON)  -> BT param = kaguya bag
    /// th=1 (セーラーON)  -> BT[kaguya bag] param = kaguya bag
    /// </code>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameBlendTreeOverrideAttribute : Attribute
    {
        /// <summary>対象の BlendTree ノードの名前 (完全一致)。</summary>
        public string TreeName { get; }

        /// <summary>そのツリーの中で値を差し替えるパラメーター名。</summary>
        public string ParameterName { get; }

        /// <summary>差し替える値。</summary>
        public float Value { get; }
        /// <summary>true にすると、ParameterName が削除対象かどうかに関わらず (削除が 1 つも無いビルドでも) 常にこの値で枝を確定させる。</summary>
        public bool Always { get; set; }

        public ReFrameBlendTreeOverrideAttribute(string treeName, string parameterName, float value)
        {
            TreeName = treeName;
            ParameterName = parameterName;
            Value = value;
        }
    }
}
