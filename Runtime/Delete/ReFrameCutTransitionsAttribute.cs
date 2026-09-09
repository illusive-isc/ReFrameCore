using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameCutTransitionsAttribute : Attribute
    {
        /// <summary>対象のパラメーター名。</summary>
        public string ParameterName { get; }

        /// <summary>指定した場合、このフィールドの Value がこの値のときだけ適用する。</summary>
        public float? OnlyWhenValue { get; }

        public ReFrameCutTransitionsAttribute()
        {
            ParameterName = null;
            OnlyWhenValue = null;
        }

        public ReFrameCutTransitionsAttribute(string parameterName)
        {
            ParameterName = parameterName;
            OnlyWhenValue = null;
        }

        public ReFrameCutTransitionsAttribute(string parameterName, float onlyWhenValue)
        {
            ParameterName = parameterName;
            OnlyWhenValue = onlyWhenValue;
        }
    }
}
