using System.Collections.Generic;
using UnityEngine;

namespace Pixel
{
    public class SpawnBlockUniqueColorFromMap : MonoBehaviour
    {
        [SerializeField] private RenderMap mapSource;
        [SerializeField] private GameObject prefab;
        [SerializeField] private Transform parent;
        
        [SerializeField] private float colorTolerance = 0.05f;
        [SerializeField] private float alphaThreshold = 0.1f;
        [SerializeField] private float spacing = 1.2f;
        [SerializeField] private int column = 5;

        private MaterialPropertyBlock _mpb;
        private PrefabPool<BlockSplineRunner> blockPool;

        private void Start()
        {
            SpawnUnique();
        }
        
        //Spawn Block bắn đạn 
            // check tranh rồi kiếm tra màu rồi add và list danh sách màu của tranh có là những màu gì
        public void SpawnUnique()
        {
            if (mapSource == null) return;
            BlockSplineRunner blockPrefab = prefab != null ? prefab.GetComponent<BlockSplineRunner>() : null;
            
            if (blockPrefab == null) return;
            blockPool ??= new PrefabPool<BlockSplineRunner>(blockPrefab, parent);

            Sprite sourceSprite = mapSource.SourceSprite;
            if (sourceSprite == null) return;

            Texture2D tex = sourceSprite.texture;
            Rect rect = sourceSprite.rect;
            List<ColorGroup> colorGroups = new List<ColorGroup>();

            for (int x = 0; x < rect.width; x++)
            {
                for (int y = 0; y < rect.height; y++)
                {
                    Color color = tex.GetPixel((int)rect.x + x, (int)rect.y + y);
                    if (color.a < alphaThreshold) continue;
                    AddOrIncreaseColorGroup(colorGroups, color);
                }
            }
            for (int i = 0; i < colorGroups.Count; i++)
            {
                int x = i % column;
                int y = i / column;

                BlockSplineRunner runner = blockPool.Get();
                runner.SetPool(blockPool);
                GameObject obj = runner.gameObject;
                obj.transform.SetParent(parent, false);

                obj.transform.localPosition = new Vector3(x * spacing, y * spacing, 0);
                obj.transform.localRotation = Quaternion.Euler(90, 0, 0);

                SetColor(obj, colorGroups[i].Color);
                InitializeBlock(runner, colorGroups[i].Count);
            }
        }

        // danh sách màu 
        private void AddOrIncreaseColorGroup(List<ColorGroup> groups, Color target)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if (!IsSimilar(groups[i].Color, target)) continue;
                ColorGroup group = groups[i];
                group.Count++;
                groups[i] = group;
                return;
            }
            groups.Add(new ColorGroup(target, 1));
        }

        // độ lệch màu 
        private bool IsSimilar(Color a, Color b)
        {
            return Vector3.Distance(
                new Vector3(a.r, a.g, a.b),
                new Vector3(b.r, b.g, b.b)
            ) <= colorTolerance;
        }
        // Set màu mesh cho block bắn đạn 
        private void SetColor(GameObject obj, Color color)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            var renderer = obj.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            
            renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(_mpb);
        }

        private void InitializeBlock(BlockSplineRunner runner, int shotCount)
        {
            if (runner == null) return;
            runner.InitializeShotLimit(shotCount);
        }
    }
}
