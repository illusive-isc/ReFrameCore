using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameUnsyncParameterAttribute : Attribute
    {
        /// <summary>対象のパラメーター名。</summary>
        public string ParameterName { get; }

        public ReFrameUnsyncParameterAttribute()
        {
            ParameterName = null;
        }

        public ReFrameUnsyncParameterAttribute(string parameterName)
        {
            ParameterName = parameterName;
        }
    }
}
