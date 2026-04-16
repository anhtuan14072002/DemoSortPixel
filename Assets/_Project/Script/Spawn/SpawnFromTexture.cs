using UnityEngine;

public class SpawnFromTexture : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private Sprite sourceSprite;

    [Header("Spawn")]
    [SerializeField] private GameObject prefab;
    [SerializeField] private Transform parent;

    [Header("Setting")]
    [SerializeField] private float pixelSize = 0.1f;
    [SerializeField] private float spacing = 0.02f;
    [SerializeField] private float alphaThreshold = 0.1f;

    public Sprite SourceSprite => sourceSprite;

    private void Start()
    {
        Spawn();
    }

    public void Spawn()
    {
        if (sourceSprite == null || prefab == null)
            return;

        Texture2D tex = sourceSprite.texture;
        Rect rect = sourceSprite.rect;

        float step = pixelSize + spacing;
        Vector3 offset = new Vector3(rect.width, rect.height, 0) * step * 0.5f;

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

                GameObject obj = Instantiate(prefab, parent);

                obj.transform.localPosition = new Vector3(
                    x * step,
                    y * step,
                    0
                ) - offset;

                obj.transform.localRotation = Quaternion.Euler(120, 0, 0);

                SetColor(obj, color);
                RegisterCellColor(obj, color);
            }
        }
    }

    private void SetColor(GameObject obj, Color color)
    {
        var renderer = obj.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        Material mat = renderer.material;
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }
        else if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", color);
        }
    }

    private void RegisterCellColor(GameObject obj, Color color)
    {
        ColorCell colorCell = obj.GetComponent<ColorCell>();
        if (colorCell == null)
        {
            colorCell = obj.AddComponent<ColorCell>();
        }

        colorCell.SetColor(color);
    }
}