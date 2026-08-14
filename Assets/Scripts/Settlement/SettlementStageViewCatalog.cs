using System;
using UnityEngine;

namespace GuildIdle.Settlement
{
    [CreateAssetMenu(fileName = "SettlementStageViewCatalog", menuName = "GuildIdle/Settlement Stage View Catalog")]
    public sealed class SettlementStageViewCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class CameraPreset
        {
            [SerializeField] private string stageId;
            [SerializeField] private Vector3 position;
            [SerializeField] private Vector3 eulerAngles;
            [SerializeField] private bool orthographic;
            [SerializeField] private float fieldOfView = 60f;
            [SerializeField] private float orthographicSize = 5f;

            public string StageId => stageId;

            public void Apply(Camera target)
            {
                if (target == null)
                    throw new ArgumentNullException(nameof(target));

                target.transform.SetPositionAndRotation(position, Quaternion.Euler(eulerAngles));
                target.orthographic = orthographic;
                if (orthographic)
                    target.orthographicSize = orthographicSize;
                else
                    target.fieldOfView = fieldOfView;
            }
        }

        [SerializeField] private CameraPreset[] cameraPresets = Array.Empty<CameraPreset>();

        public bool TryGetCameraPreset(string stageId, out CameraPreset preset)
        {
            foreach (var candidate in cameraPresets ?? Array.Empty<CameraPreset>())
            {
                if (candidate != null && string.Equals(candidate.StageId, stageId, StringComparison.Ordinal))
                {
                    preset = candidate;
                    return true;
                }
            }

            preset = null;
            return false;
        }
    }
}
