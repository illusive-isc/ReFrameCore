using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>ReFrame の Inspector の 1 行 (ReFrameDeleteEntry) について、拡張側へ渡す情報。</summary>
    public sealed class ReFrameRowContext
    {
        public ReFrameDeleteComponent Component;
        public FieldInfo Field;
        public SerializedObject SerializedObject;
        public SerializedProperty EnabledProperty;
        public SerializedProperty ValueProperty;
        public IReadOnlyList<string> ParameterNames;
        public string Label;
    }

    /// <summary>拡張要素をどこに置くか。</summary>
    public enum ReFrameRowDecoratorPlacement
    {
        /// <summary>行のタイトルの横。</summary>
        Row,
        /// <summary>行が入っている見出し (サブ見出し) の右端。見出しの外の行なら Row と同じ。</summary>
        GroupHeader,
    }

    /// <summary>行のタイトル横 (または行の見出し) に要素を足す拡張点。ReFrameAdd 等が [InitializeOnLoad] で登録する。</summary>
    public interface IReFrameRowDecorator
    {
        /// <summary>この行に足す要素。要らなければ null。</summary>
        VisualElement Build(ReFrameRowContext context);

        /// <summary>要素の置き場所。既定は行のタイトル横。</summary>
        ReFrameRowDecoratorPlacement Placement => ReFrameRowDecoratorPlacement.Row;
    }

    public static class ReFrameRowDecorators
    {
        static readonly List<IReFrameRowDecorator> Registered = new();

        public static void Register(IReFrameRowDecorator decorator)
        {
            if (decorator != null && !Registered.Contains(decorator))
                Registered.Add(decorator);
        }

        public static void Unregister(IReFrameRowDecorator decorator) => Registered.Remove(decorator);

        internal static IEnumerable<(VisualElement Element, ReFrameRowDecoratorPlacement Placement)> Build(ReFrameRowContext context)
        {
            foreach (var decorator in Registered)
            {
                var element = decorator.Build(context);
                if (element != null)
                    yield return (element, decorator.Placement);
            }
        }
    }
}
