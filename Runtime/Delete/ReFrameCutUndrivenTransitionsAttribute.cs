using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameCutUndrivenTransitionsAttribute : Attribute
    {
        /// <summary>対象のパラメーター名。</summary>
        public string ParameterName { get; }

        /// <summary>true にすると、遷移を丸ごと消すのではなく既定値に固定されたものとして条件を評価し直す。</summary>
        public bool EvaluateAsFixed { get; set; }

        public ReFrameCutUndrivenTransitionsAttribute(string parameterName)
        {
            ParameterName = parameterName;
        }
    }
}
