using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameDeleteLayerAttribute : Attribute
    {
        /// <summary>探して削除するレイヤーの名前 (完全一致)。</summary>
        public string Name { get; }

        /// <summary>指定した場合、このフィールドの Value がこの値のときだけ対象にする。</summary>
        public float? OnlyWhenValue { get; }

        public ReFrameDeleteLayerAttribute(string name)
        {
            Name = name;
            OnlyWhenValue = null;
        }

        public ReFrameDeleteLayerAttribute(string name, float onlyWhenValue)
        {
            Name = name;
            OnlyWhenValue = onlyWhenValue;
        }
    }
}
