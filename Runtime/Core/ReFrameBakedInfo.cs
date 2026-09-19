using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrame の焼き込みを済ませたアバターに残す記録。削除の設定は外してあり、ビルドでは Optimizing の掃除 (Sweep) だけがここから材料を受け取って動く。</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class ReFrameBakedInfo : MonoBehaviour, IEditorOnly
    {
        /// <summary>焼き付け前に Transform を使っていたコンポーネント群。全部消えて初めて「使われなくなった」とみなす。</summary>
        [Serializable]
        public struct BoneOwner
        {
            public Transform bone;
            public string reason;
            public UnityEngine.Object[] owners;
        }

        public string reframeVersion;
        public string bakedAt;
        public string buildTarget;
        public string sourcePrefab;
        public string assetFolder;
        public string[] components = new string[0];
        public string[] entries = new string[0];

        public ReFrameSweepMode sweepMode = ReFrameSweepMode.Sweep;

        [HideInInspector] public bool sweepSnapshotTaken;
        [HideInInspector] public List<GameObject> sweepActiveSelf = new List<GameObject>();
        [HideInInspector] public List<Transform> sweepActiveAnimated = new List<Transform>();
        [HideInInspector] public List<Renderer> sweepEnabledRenderers = new List<Renderer>();
        [HideInInspector] public List<Renderer> sweepEnabledAnimated = new List<Renderer>();
        [HideInInspector] public List<BoneOwner> sweepBoneOwners = new List<BoneOwner>();

        public bool SweepUnusedObjects => sweepMode == ReFrameSweepMode.Sweep;
    }
}
