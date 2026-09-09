using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。</summary>
    /// <code>
    /// [ReFrameGroupOrder("closet", "Gimmick", "もちまる", "FireGun", "Particle")]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class ReFrameGroupOrderAttribute : Attribute
    {
        /// <summary>並べたい順のグループ名。</summary>
        public string[] Order { get; }

        public ReFrameGroupOrderAttribute(params string[] order)
        {
            Order = order ?? new string[0];
        }

        /// <summary>並び替えに使う順位。</summary>
        public int IndexOf(string groupName)
        {
            for (var i = 0; i < Order.Length; i++)
                if (Order[i] == groupName)
                    return i;
            return int.MaxValue;
        }
    }
}
