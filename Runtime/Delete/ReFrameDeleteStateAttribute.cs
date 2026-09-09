using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameDeleteStateAttribute : Attribute
    {
        /// <summary>対象のレイヤー名 (完全一致)。</summary>
        public string LayerName { get; }

        /// <summary>取り除くステート名 (完全一致)。</summary>
        public string StateName { get; }

        /// <summary>指定した場合、このフィールドの Value がこの値のときだけ削除する。</summary>
        public float? OnlyWhenValue { get; }

        public ReFrameDeleteStateAttribute(string layerName, string stateName)
        {
            LayerName = layerName;
            StateName = stateName;
            OnlyWhenValue = null;
        }

        public ReFrameDeleteStateAttribute(string layerName, string stateName, float onlyWhenValue)
        {
            LayerName = layerName;
            StateName = stateName;
            OnlyWhenValue = onlyWhenValue;
        }
    }
}
