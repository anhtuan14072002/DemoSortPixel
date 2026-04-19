using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace Pixel
{
    public class BlockSpawnButton : MonoBehaviour
    {
        [Header("Button")]
        [SerializeField] private Button spawnButton;

        [Header("Block")]
        [SerializeField] private GameObject blockPrefab;
        [SerializeField] private Transform parent;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private int bulletCount = 10;
        [SerializeField] private Color blockColor = Color.white;

        [Inject] private DiContainer _container;

        private MaterialPropertyBlock mpb;
        private PrefabPool<BlockSplineRunner> blockPool;
        private BlockSplineRunner blockPrefabComponent;

        private void Awake()
        {
            mpb = new MaterialPropertyBlock();
            ResolvePrefab();
        }

        private void OnEnable()
        {
            if (spawnButton != null)
                spawnButton.onClick.AddListener(SpawnConfiguredBlock);
        }

        private void OnDisable()
        {
            if (spawnButton != null)
                spawnButton.onClick.RemoveListener(SpawnConfiguredBlock);
        }

        public void SpawnConfiguredBlock()
        {
            BlockSplineRunner runner = SpawnBlock(bulletCount, blockColor);
            if (runner != null)
                runner.TryRun();
        }

        public BlockSplineRunner SpawnBlock(int shotCount, Color color)
        {
            ResolvePrefab();

            if (blockPrefabComponent == null)
                return null;

            blockPool ??= new PrefabPool<BlockSplineRunner>(blockPrefabComponent, parent);

            BlockSplineRunner runner = blockPool.Get();
            runner.SetPool(blockPool);

            _container.Inject(runner);

            Transform runnerTransform = runner.transform;
            runnerTransform.SetParent(parent, false);

            if (spawnPoint != null)
                runnerTransform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);

            ApplyColor(runner.gameObject, color);
            runner.InitializeShotLimit(shotCount);

            return runner;
        }

        private void ResolvePrefab()
        {
            if (blockPrefabComponent != null) return;
            blockPrefabComponent = blockPrefab != null ? blockPrefab.GetComponent<BlockSplineRunner>() : null;
        }

        private void ApplyColor(GameObject target, Color color)
        {
            if (target == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            MeshRenderer meshRenderer = target.GetComponent<MeshRenderer>();
            if (meshRenderer == null) return;

            meshRenderer.GetPropertyBlock(mpb);

            if (meshRenderer.sharedMaterial != null && meshRenderer.sharedMaterial.HasProperty("_BaseColor"))
                mpb.SetColor("_BaseColor", color);
            else if (meshRenderer.sharedMaterial != null && meshRenderer.sharedMaterial.HasProperty("_Color"))
                mpb.SetColor("_Color", color);

            meshRenderer.SetPropertyBlock(mpb);
        }
    }
}