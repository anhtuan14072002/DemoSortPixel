using System.Collections.Generic;
using UnityEngine;

public class SpawnUniqueColorFromMap : MonoBehaviour
{
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
        if (mapSource == null) return;
        Sprite sourceSprite = mapSource.SourceSprite;
        if (sourceSprite == null) return;

        Texture2D tex = sourceSprite.texture;
        Rect rect = sourceSprite.rect;

        List<Color> uniqueColors = new List<Color>();

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

                if (!IsColorExist(uniqueColors, color))
                {
                    uniqueColors.Add(color);
                }
            }
        }

        for (int i = 0; i < uniqueColors.Count; i++)
        {
            int x = i % column;
            int y = i / column;

            GameObject obj = Instantiate(prefab, parent);

            obj.transform.localPosition = new Vector3(
                x * spacing,
                y * spacing,
                0
            );

            obj.transform.localRotation = Quaternion.identity;

            SetColor(obj, uniqueColors[i]);
        }
    }

    private bool IsColorExist(List<Color> list, Color target)
    {
        foreach (var c in list)
        {
            if (IsSimilar(c, target))
                return true;
        }
        return false;
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
        if (renderer == null) return;

        renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(_mpb);
    }
}