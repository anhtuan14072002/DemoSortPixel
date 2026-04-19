using UnityEngine;

namespace Pixel
{
    public class RenderMap : MonoBehaviour
    {
        [SerializeField] private Sprite _sourceSprite;
        [SerializeField] private GameObject _cellPrefab;
        [SerializeField] private Transform _posMap;

        [SerializeField] private float _pixelSize = 0.1f;
        [SerializeField] private float _spacing = 0.02f;
        [SerializeField] private float _alphaThreshold = 0.1f;

        [SerializeField] private float _spriteSizeThreshold = 20f;
        [SerializeField] private float _parentScaleAtOrBelowThreshold = 1f;
        [SerializeField] private float _parentScaleAboveThreshold = 0.8f;

        public Sprite SourceSprite => _sourceSprite;
        private PrefabPool<ColorCell> _cellPool;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Input.multiTouchEnabled = false;
        }

        private void Start()
        {
            Spawn();
        }

        public void Spawn()
        {
            if (_sourceSprite == null || _cellPrefab == null) return;
            ColorCell cellPrefab = _cellPrefab.GetComponent<ColorCell>();
            if (cellPrefab == null)
            {
                Debug.LogWarning($"{nameof(RenderMap)} requires _cellPrefab to contain {nameof(ColorCell)} for pooling.", this);
                return;
            }

            _cellPool ??= new PrefabPool<ColorCell>(cellPrefab, _posMap);

            Texture2D tex = _sourceSprite.texture;
            Rect rect = _sourceSprite.rect;

            ApplyParentScale(rect);

            float step = _pixelSize + _spacing;
            Vector3 offset = new Vector3(rect.width, rect.height, 0) * step * 0.5f;

            for (int x = 0; x < rect.width; x++)
            {
                for (int y = 0; y < rect.height; y++)
                {
                    Color color = tex.GetPixel((int)rect.x + x, (int)rect.y + y );

                    if (color.a < _alphaThreshold) continue;
                    ColorCell cell = _cellPool.Get();
                    cell.SetPool(_cellPool);
                    var obj = cell.gameObject;
                    obj.transform.SetParent(_posMap, false);

                    obj.transform.localPosition = new Vector3(x * step, y * step, 0) - offset;
                    obj.transform.localRotation = Quaternion.Euler(120, 0, 0);

                    SetColor(obj, color);
                    RegisterCellColor(cell, color);
                }
            }
        }

        // Check xem texture có kích thước là bao nhiêu rồi scale parent bằng bieesn cố định để bé map lại
        private void ApplyParentScale(Rect rect)
        {
            if (_posMap == null) return;
            
            bool isAtOrBelowThreshold = rect.width <= _spriteSizeThreshold && rect.height <= _spriteSizeThreshold;
            float scale = isAtOrBelowThreshold ? _parentScaleAtOrBelowThreshold : _parentScaleAboveThreshold;
            _posMap.localScale = Vector3.one * scale;
        }

        // gán màu cho mesh  trong cell 
        private void SetColor(GameObject obj, Color color)
        {
            var renderer = obj.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            Material mat = renderer.material;
            
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
        }

        // Set màu cho Cell
        private void RegisterCellColor(ColorCell colorCell, Color color)
        {
            if (colorCell == null) return;
            colorCell.SetColor(color);
        }
    }
}
