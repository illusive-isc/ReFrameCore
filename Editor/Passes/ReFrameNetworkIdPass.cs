using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRC.SDKBase.Network;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>PhysBone のネットワーク ID を、階層のパスから決まる値で埋める。</summary>
    internal class ReFrameNetworkIdFinalizePass : Pass<ReFrameNetworkIdFinalizePass>
    {
        public override string DisplayName => "ReFrame: ネットワーク ID の最終確認";

        protected override void Execute(BuildContext context) =>
            ReFrameNetworkIdPass.Finalize(context);
    }

    internal class ReFrameNetworkIdPass : Pass<ReFrameNetworkIdPass>
    {
        public override string DisplayName => "ReFrame: ネットワーク ID の割り当て";

        /// <summary>VRCQuestTools の NetworkIDAssigner。</summary>
        static readonly System.Type NetworkIdAssignerType = System.Type.GetType(
            "KRT.VRCQuestTools.Components.NetworkIDAssigner, VRCQuestTools"
        );

        protected override void Execute(BuildContext context)
        {
            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
                return;

            if (
                NetworkIdAssignerType != null
                && context.AvatarRootObject.GetComponent(NetworkIdAssignerType) != null
            )
                return;

            Assign(descriptor);
        }

        /// <summary>ID が未割り当てのものだけを埋める。</summary>
        internal static int Assign(VRCAvatarDescriptor descriptor)
        {
            var targets = new List<INetworkID>();
            descriptor.GetNetworkIDObjects(targets);

            var collection = descriptor.NetworkIDCollection ??= new List<NetworkIDPair>();

            var added = 0;
            using (var sha1 = SHA1.Create())
            {
                int HashOf(INetworkID netId) =>
                    IdFromPath(
                        sha1,
                        ReFramePhysBoneCatalog.PathOf(
                            descriptor.transform,
                            ((MonoBehaviour)netId).transform
                        )
                    );

                targets.Sort((a, b) => HashOf(a) - HashOf(b));

                foreach (var netId in targets)
                {
                    var behaviour = netId as MonoBehaviour;
                    if (behaviour == null)
                        continue;

                    if (FindId(collection, behaviour.gameObject).HasValue)
                        continue;

                    var id = HashOf(netId);

                    var guard = 0;
                    while (
                        collection.Exists(pair => pair.ID == id)
                        && guard++ <= NetworkIDAssignment.MaxID - NetworkIDAssignment.MinID
                    )
                    {
                        id++;
                        if (id > NetworkIDAssignment.MaxID)
                            id = NetworkIDAssignment.MinID;
                    }

                    collection.Add(new NetworkIDPair { gameObject = behaviour.gameObject, ID = id });
                    added++;
                }
            }

            collection.RemoveAll(pair =>
                pair.gameObject == null || pair.gameObject.GetComponent<INetworkID>() == null
            );

            return added;
        }

        /// <summary>AvatarOptimizer より後で、もう一度 Assign を掛ける。</summary>
        internal static void Finalize(BuildContext context)
        {
            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
                return;
            if (
                NetworkIdAssignerType != null
                && context.AvatarRootObject.GetComponent(NetworkIdAssignerType) != null
            )
                return;
            var before = (descriptor.NetworkIDCollection ?? new List<NetworkIDPair>()).Count;
            var added = Assign(descriptor);
            var after = descriptor.NetworkIDCollection.Count;
            if (added > 0 || after != before)
                Debug.LogWarning(
                    $"[ReFrameCore] ネットワーク ID を最終確認しました: 追加 {added} 件、消えた実体の登録を落として {before} → {after} 件。"
                );
        }

        /// <summary>その GameObject に振られている ID。</summary>
        static int? FindId(List<NetworkIDPair> collection, GameObject target)
        {
            foreach (var pair in collection)
                if (pair.gameObject == target)
                    return pair.ID;
            return null;
        }

        /// <summary>階層のパスから ID を作る。</summary>
        static int IdFromPath(SHA1 sha1, string path)
        {
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(path));
            var value =
                ((hash[0] & 0x7f) << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3];
            var range = NetworkIDAssignment.MaxID - NetworkIDAssignment.MinID + 1;
            return NetworkIDAssignment.MinID + (value % range);
        }
    }
}
