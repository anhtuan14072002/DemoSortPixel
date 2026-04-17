using System.Collections.Generic;
using UnityEngine;

public class SpawnUniqueColorFromMap : MonoBehaviour
{
    private struct ColorGroup
    {
        public Color Color;
        public int Count;

        public ColorGroup(Color color, int count)
        {
            Color = color;
            Count = count;
        }
    }

    [Header("Ref")]
    [SerializeField] private SpawnFromTexture mapSource;

    [Header("Spawn")]
    [SerializeField] private GameObject prefab;
    [SerializeField] private Transform parent;

    [Header("Layout")]
    [SerializeField] private float spacing = 1.2f;
    [SerializeField] private int column = 5;

    [Header("Color Setting")]
    [SerializeField] private float colorTolerance = 0.05f;
    [SerializeField] private float alphaThreshold = 0.1f;

    private MaterialPropertyBlock _mpb;

    private void Start()
    {
        SpawnUnique();
    }

    public void SpawnUnique()
    {
        if (mapSource == null)
            return;

        Sprite sourceSprite = mapSource.SourceSprite;
        if (sourceSprite == null)
            return;

        Texture2D tex = sourceSprite.texture;
        Rect rect = sourceSprite.rect;

        List<ColorGroup> colorGroups = new List<ColorGroup>();

        for (int x = 0; x < rect.width; x++)
        {
            for (int y = 0; y < rect.height; y++)
            {
                Color color = tex.GetPixel(
                    (int)rect.x + x,
                    (int)rect.y + y
                );

                if (color.a < alphaThreshold)
                    continue;

                AddOrIncreaseColorGroup(colorGroups, color);
            }
        }

        for (int i = 0; i < colorGroups.Count; i++)
        {
            int x = i % column;
            int y = i / column;

            GameObject obj = Instantiate(prefab, parent);

            obj.transform.localPosition = new Vector3(
                x * spacing,
                y * spacing,
                0
            );

            obj.transform.localRotation = Quaternion.Euler(90, 0, 0);

            SetColor(obj, colorGroups[i].Color);
            InitializeBlock(obj, colorGroups[i].Count);
        }
    }

    private void AddOrIncreaseColorGroup(List<ColorGroup> groups, Color target)
    {
        for (int i = 0; i < groups.Count; i++)
        {
            if (!IsSimilar(groups[i].Color, target))
                continue;

            ColorGroup group = groups[i];
            group.Count++;
            groups[i] = group;
            return;
        }

        groups.Add(new ColorGroup(target, 1));
    }

    private bool IsSimilar(Color a, Color b)
    {
        return Vector3.Distance(
            new Vector3(a.r, a.g, a.b),
            new Vector3(b.r, b.g, b.b)
        ) <= colorTolerance;
    }

    private void SetColor(GameObject obj, Color color)
    {
        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        var renderer = obj.GetComponent<MeshRenderer>();
        if (renderer == null)
            return;

        renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(_mpb);
    }

    private void InitializeBlock(GameObject obj, int shotCount)
    {
        BlockSplineRunner runner = obj.GetComponent<BlockSplineRunner>();
        if (runner == null)
            return;

        runner.InitializeShotLimit(shotCount);
    }
}
