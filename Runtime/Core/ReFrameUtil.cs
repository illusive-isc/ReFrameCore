using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrame 共通関数。</summary>
    public static class ReFrameUtil
    {

        /// <summary>root からの相対パス (オブジェクト名の配列) で Transform を取得する。</summary>
        public static Transform Find(Transform root, params string[] path)
        {
            if (root == null || path == null || path.Length == 0)
                return null;
            var current = root;
            foreach (var name in path)
            {
                current = FindChild(current, name);
                if (current == null)
                    return null;
            }
            return current;
        }

        /// <summary>直下の子を名前で取得する。</summary>
        public static Transform FindChild(Transform parent, string name)
        {
            if (parent == null)
                return null;
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
            }
            return null;
        }

        /// <summary>名前でパラメーターを取得する。</summary>
        public static VRCExpressionParameters.Parameter GetParameter(
            VRCExpressionParameters parameters,
            string name
        )
        {
            if (parameters == null || parameters.parameters == null)
                return null;
            return Array.Find(parameters.parameters, p => p != null && p.name == name);
        }

        /// <summary>GameObject の active を変更する。</summary>
        public static bool SetActive(GameObject obj, bool active)
        {
            if (obj == null)
                return false;
            obj.SetActive(active);
            return true;
        }

        /// <summary>コンポーネントの enabled を変更する。</summary>
        public static bool SetEnabled(Component component, bool enabled)
        {
            switch (component)
            {
                case Behaviour behaviour:
                    behaviour.enabled = enabled;
                    return true;
                case Renderer renderer:
                    renderer.enabled = enabled;
                    return true;
                case Collider collider:
                    collider.enabled = enabled;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>SkinnedMeshRenderer の BlendShape ウェイトを変更する。</summary>
        public static bool SetBlendShapeWeight(
            SkinnedMeshRenderer renderer,
            string blendShapeName,
            float weight
        )
        {
            if (renderer == null || renderer.sharedMesh == null)
                return false;
            var index = renderer.sharedMesh.GetBlendShapeIndex(blendShapeName);
            if (index < 0)
                return false;
            renderer.SetBlendShapeWeight(index, weight);
            return true;
        }

        /// <summary>Renderer の指定インデックスのマテリアルを変更する。</summary>
        public static bool SetMaterial(Renderer renderer, int index, Material material)
        {
            if (renderer == null)
                return false;
            var mats = renderer.sharedMaterials;
            if (index < 0 || index >= mats.Length)
                return false;
            mats[index] = material;
            renderer.sharedMaterials = mats;
            return true;
        }

        /// <summary>Transform の localPosition の 1 軸 (0=x, 1=y, 2=z) を変更する。</summary>
        public static bool SetLocalPositionAxis(Transform target, int axis, float value)
        {
            if (target == null)
                return false;
            var v = target.localPosition;
            v[axis] = value;
            target.localPosition = v;
            return true;
        }

        /// <summary>Transform の localScale の 1 軸 (0=x, 1=y, 2=z) を変更する。</summary>
        public static bool SetLocalScaleAxis(Transform target, int axis, float value)
        {
            if (target == null)
                return false;
            var v = target.localScale;
            v[axis] = value;
            target.localScale = v;
            return true;
        }

        /// <summary>Transform の localEulerAngles の 1 軸 (0=x, 1=y, 2=z) を変更する。</summary>
        public static bool SetLocalEulerAnglesAxis(Transform target, int axis, float value)
        {
            if (target == null)
                return false;
            var v = target.localEulerAngles;
            v[axis] = value;
            target.localEulerAngles = v;
            return true;
        }

        /// <summary>Transform の localRotation (Quaternion) の 1 成分 (0=x, 1=y, 2=z, 3=w) を変更する。</summary>
        public static bool SetLocalRotationComponent(Transform target, int component, float value)
        {
            if (target == null)
                return false;
            var q = target.localRotation;
            switch (component)
            {
                case 0:
                    q.x = value;
                    break;
                case 1:
                    q.y = value;
                    break;
                case 2:
                    q.z = value;
                    break;
                case 3:
                    q.w = value;
                    break;
                default:
                    return false;
            }
            target.localRotation = q;
            return true;
        }

        /// <summary>指定した名前のパラメーターをまとめて削除する。</summary>
        public static int RemoveParameter(VRCExpressionParameters parameters, params string[] names)
        {
            if (parameters == null || parameters.parameters == null || names == null || names.Length == 0)
                return 0;
            var nameSet = new HashSet<string>(names);
            var filtered = parameters
                .parameters.Where(p => p == null || !nameSet.Contains(p.name))
                .ToArray();
            var removed = parameters.parameters.Length - filtered.Length;
            if (removed > 0)
                parameters.parameters = filtered;
            return removed;
        }
    }
}
