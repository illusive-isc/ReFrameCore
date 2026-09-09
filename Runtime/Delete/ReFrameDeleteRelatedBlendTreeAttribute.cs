using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameDeleteRelatedBlendTreeAttribute : Attribute
    {
        /// <summary>探して削除する BlendTree の Name (完全一致)。</summary>
        public string Name { get; }

        /// <summary>指定した場合、このフィールドの Value がこの値のときだけ対象にする。</summary>
        public float? OnlyWhenValue { get; }

        /// <summary>true にすると、削除する前にそのノード自身の BlendParameter の (AnimatorController 上の) デフォルト値で解決した見た目を焼き付けてから削除する (Simple1D のみ対応)。</summary>
        public bool Bake { get; set; }

        /// <summary>true にすると、フィールドの Enabled に関わらず常に削除する。</summary>
        public bool Always { get; set; }

        public ReFrameDeleteRelatedBlendTreeAttribute(string name)
        {
            Name = name;
            OnlyWhenValue = null;
        }

        public ReFrameDeleteRelatedBlendTreeAttribute(string name, float onlyWhenValue)
        {
            Name = name;
            OnlyWhenValue = onlyWhenValue;
        }
    }
}
