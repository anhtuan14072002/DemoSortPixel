using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace Sand
{
    public class SpawnManager : MonoBehaviour
    {
        [SerializeField] private GameObject _blockPrefab;
        [SerializeField] private Transform _spawnRoot;
        [SerializeField] private int _maxBlocksPerRow = 5;
        [SerializeField] private Vector2 _blockSpacing = new Vector2(1.25f, 1.25f);
        [SerializeField] private Vector3 _spawnOrigin;
        [SerializeField] private SplineContainer _splineContainer;

        public void SpawnBlocks(IReadOnlyList<ColorGroup> groups)
        {
            if (_blockPrefab == null || groups == null) return;

            ClearSpawnedBlocks();

            for (int i = 0; i < groups.Count; i++)
            {
                SpawnBlock(groups[i], i);
            }
        }

        private void SpawnBlock(ColorGroup group, int index)
        {
            if (_blockPrefab == null || group == null) return;

            var parent = _spawnRoot != null ? _spawnRoot : transform;
            var block = Instantiate(_blockPrefab, parent);
            block.transform.localPosition = GetLocalSpawnPosition(index);

            var blockInfo = block.GetComponent<BlockInfo>();
            if (blockInfo != null)
            {
                blockInfo.Configure(group);
                blockInfo.SetSplineContainer(_splineContainer);
            }
        }

        private void ClearSpawnedBlocks()
        {
            var parent = _spawnRoot != null ? _spawnRoot : transform;

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        private Vector3 GetLocalSpawnPosition(int index)
        {
            int columns = Mathf.Max(1, _maxBlocksPerRow);
            int column = index % columns;
            int row = index / columns;

            return _spawnOrigin + new Vector3(
                column * _blockSpacing.x,
                -row * _blockSpacing.y,
                0f
            );
        }
    }
}