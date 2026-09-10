using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。指定した [ReFrameDelete] ParameterName を持つ行と相互に道連れにする (片方を ON にするともう片方も ON、2D ブレンドの両軸など一緒でないと意味が無い組み合わせ用)。</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public sealed class ReFrameLinkedWithAttribute : Attribute
    {
        public string ParameterName { get; }

        public ReFrameLinkedWithAttribute(string parameterName)
        {
            ParameterName = parameterName;
        }
    }
}
