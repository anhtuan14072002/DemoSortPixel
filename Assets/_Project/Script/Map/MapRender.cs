using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sand
{
    public class MapRender : MonoBehaviour
    {
        [SerializeField] private Sprite _sourceSprite;
        [SerializeField] private SpriteRenderer _mapRenderer;
        [SerializeField] private SpawnManager _spawnManager;
        [SerializeField] public int _hight;
        [SerializeField] public int _wight;
        [SerializeField] private int _pixelsPerCell;
        [SerializeField] private float _mergeColorDistance = 50f;
        [SerializeField] private float _clearColorDistance = 50f;
        [SerializeField] private float _splineDuration = 0.4f;
        [SerializeField] private float _raycastInset = 0.25f;

        public Map _map;
        private readonly List<ColorGroup> _colorGroups = new List<ColorGroup>();
        private BoxCollider _mapCollider;

        private BlockInfo _activeBlock;
        private readonly HashSet<Vector2Int> _clearedCellsThisMove = new HashSet<Vector2Int>();

        private void Awake()
        {
            _mapRenderer = GetComponent<SpriteRenderer>();
            if (_mapRenderer == null) _mapRenderer = gameObject.AddComponent<SpriteRenderer>();

            _map = new Map(_wight, _hight, _pixelsPerCell);

            if (_sourceSprite != null)
            {
                _map.LoadFromSprite(_sourceSprite);
                SpawnColorBlocks();
            }

            _map.RebuildTexture();
            _map.ApplyTexture(_mapRenderer);
            EnsureMapCollider();

            Application.targetFrameRate = 60;
            Input.multiTouchEnabled = false;
        }

        private void Update()
        {
            if (Mouse.current == null) return;
            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            var cameraToUse = Camera.main;
            if (cameraToUse == null) return;

            Ray ray = cameraToUse.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit)) return;

            var block = hit.collider.GetComponentInParent<BlockInfo>();
            if (block == null) return;
            if (block.IsAnimating) return;

            HandleBlockSelected(block);
        }

        private void LateUpdate()
        {
            if (_activeBlock == null) return;
            if (!_activeBlock.IsAnimating)
            {
                _activeBlock = null;
                _clearedCellsThisMove.Clear();
                return;
            }

            TryClearCellUnderActiveBlock();
        }

        private void SpawnColorBlocks()
        {
            if (_spawnManager == null) return;

            _colorGroups.Clear();
            _colorGroups.AddRange(Map.ExtractColorGroups(_sourceSprite, _mergeColorDistance));
            _spawnManager.SpawnBlocks(_colorGroups);
        }

        private void HandleBlockSelected(BlockInfo block)
        {
            if (block == null || block.IsAnimating) return;

            _activeBlock = block;
            _clearedCellsThisMove.Clear();

            block.PlaySpline();
        }

        private void TryClearCellUnderActiveBlock()
        {
            if (_activeBlock == null) return;

            Vector3 worldPosition = _activeBlock.GetWorldPosition();
            if (!TryRaycastIntoMap(worldPosition, out var hit)) return;
            if (!TryWorldToCell(hit.point, out int cellX, out int cellY)) return;

            var cellKey = new Vector2Int(cellX, cellY);
            if (_clearedCellsThisMove.Contains(cellKey)) return;

            if (!_map.HasValue(cellX, cellY)) return;

            Color32 cellColor = _map.GetCellColor(cellX, cellY);
            if (ColorDistance(cellColor, _activeBlock.BlockColor) > _clearColorDistance) return;

            _map.ClearPixelCell(cellX, cellY, Color.clear);
            _map.RebuildTexture();
            _map.ApplyTexture(_mapRenderer);
            EnsureMapCollider();

            _clearedCellsThisMove.Add(cellKey);
        }

        private bool TryRaycastIntoMap(Vector3 worldPosition, out RaycastHit hit)
        {
            EnsureMapCollider();
            hit = default;
            if (_mapCollider == null) return false;

            Vector3 mapCenter = _mapRenderer.bounds.center;
            Vector3 direction = (mapCenter - worldPosition).normalized;
            Vector3 rayStart = worldPosition - direction * _raycastInset;
            float maxDistance = Vector3.Distance(rayStart, mapCenter) + _raycastInset * 2f;

            var hits = Physics.RaycastAll(rayStart, direction, maxDistance);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider != _mapCollider) continue;
                hit = hits[i];
                return true;
            }

            return false;
        }

        private Vector3 GetNearestBorderPoint(Vector3 worldPosition)
        {
            Bounds bounds = _mapRenderer.bounds;

            float distanceToLeft = Mathf.Abs(worldPosition.x - bounds.min.x);
            float distanceToRight = Mathf.Abs(worldPosition.x - bounds.max.x);
            float distanceToBottom = Mathf.Abs(worldPosition.y - bounds.min.y);
            float distanceToTop = Mathf.Abs(worldPosition.y - bounds.max.y);

            float minDistance = distanceToLeft;
            Vector3 borderPoint = new Vector3(
                bounds.min.x,
                Mathf.Clamp(worldPosition.y, bounds.min.y, bounds.max.y),
                bounds.center.z
            );

            if (distanceToRight < minDistance)
            {
                minDistance = distanceToRight;
                borderPoint = new Vector3(
                    bounds.max.x,
                    Mathf.Clamp(worldPosition.y, bounds.min.y, bounds.max.y),
                    bounds.center.z
                );
            }

            if (distanceToBottom < minDistance)
            {
                minDistance = distanceToBottom;
                borderPoint = new Vector3(
                    Mathf.Clamp(worldPosition.x, bounds.min.x, bounds.max.x),
                    bounds.min.y,
                    bounds.center.z
                );
            }

            if (distanceToTop < minDistance)
            {
                borderPoint = new Vector3(
                    Mathf.Clamp(worldPosition.x, bounds.min.x, bounds.max.x),
                    bounds.max.y,
                    bounds.center.z
                );
            }

            return borderPoint;
        }

        private bool TryWorldToCell(Vector3 worldPoint, out int x, out int y)
        {
            x = 0;
            y = 0;

            if (_mapRenderer.sprite == null) return false;

            Vector3 localPoint = _mapRenderer.transform.InverseTransformPoint(worldPoint);
            Bounds spriteBounds = _mapRenderer.sprite.bounds;

            float normalizedX = Mathf.InverseLerp(spriteBounds.min.x, spriteBounds.max.x, localPoint.x);
            float normalizedY = Mathf.InverseLerp(spriteBounds.min.y, spriteBounds.max.y, localPoint.y);

            x = Mathf.Clamp(Mathf.FloorToInt(normalizedX * _map.Width), 0, _map.Width - 1);
            y = Mathf.Clamp(Mathf.FloorToInt(normalizedY * _map.Height), 0, _map.Height - 1);
            return true;
        }

        private void EnsureMapCollider()
        {
            if (_mapRenderer.sprite == null) return;

            if (_mapCollider == null)
            {
                _mapCollider = GetComponent<BoxCollider>();
                if (_mapCollider == null)
                {
                    _mapCollider = gameObject.AddComponent<BoxCollider>();
                }
            }

            Bounds spriteBounds = _mapRenderer.sprite.bounds;
            _mapCollider.center = spriteBounds.center;
            _mapCollider.size = new Vector3(spriteBounds.size.x, spriteBounds.size.y, 0.1f);
        }

        private static float ColorDistance(Color32 a, Color32 b)
        {
            int dr = a.r - b.r;
            int dg = a.g - b.g;
            int db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }
    }
}