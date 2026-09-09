using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteEntry 型のフィールドに付ける。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameDeleteAttribute : Attribute
    {
        public string ParameterName { get; }

        /// <summary>Inspector の Value フィールドの表示形式。</summary>
        public ReFrameParameterType Type { get; }

        public ReFrameDeleteAttribute(string parameterName, ReFrameParameterType type = ReFrameParameterType.Auto)
        {
            ParameterName = parameterName;
            Type = type;
        }
    }
}
