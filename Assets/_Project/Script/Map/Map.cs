using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sand
{
    public class Map : IDisposable
    {
        private Texture2D Texture { get; set; }
        private bool m_isMovePause;
        private NativeArray<Cell> _cells;

        public bool IsColliding { get; private set; }
        public bool IsAnyBlockedThisTick { get; private set; }

        public bool IsMovePause
        {
            get => m_isMovePause;
            set => m_isMovePause = value;
        }

        public bool _dirty = true;

        public bool Dirty
        {
            get => _dirty;
            set => _dirty = value;
        }
        
        private bool _isGameOver;
        private int m_width, m_height;
        public int Width => m_width;
        public int Height => m_height;
        private int _pixelsPerCell = 8;
        private int Idx(int x, int y) => y * m_width + x;
        private NativeArray<Color32> _pixels;
        private Color32 m_backgroundColor;

        public Map(int width, int height, int pixelsPerCell)
        {
            m_width = width;
            m_height = height;
            _pixelsPerCell = pixelsPerCell;
            m_backgroundColor = Color.clear;

            _cells = new NativeArray<Cell>(m_width * m_height, Allocator.Persistent);
            ClearCells();

            int texW = m_width * _pixelsPerCell;
            int texH = m_height * _pixelsPerCell;

            Texture = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            Texture.filterMode = FilterMode.Point;
            Texture.wrapMode = TextureWrapMode.Clamp;
            _pixels = new NativeArray<Color32>(texW * texH, Allocator.Persistent);
        }

        public void SetUpMap(Color32 color32)
        {
            m_backgroundColor = color32;
            ClearCells();
            _dirty = true;
        }

        public void Dispose()
        {
            if (Texture != null)
                Object.Destroy(Texture);
            if (_cells.IsCreated)
                _cells.Dispose();
            if (_pixels.IsCreated)
                _pixels.Dispose();
        }

        public void ApplyTexture(SpriteRenderer render)
        {
            if (Texture == null) return;
            if (render == null) return;
            var sprite = Sprite.Create(
                Texture,
                new Rect(0, 0, Texture.width, Texture.height),
                Vector2.one * 0.5f,
                100
            );
            render.sprite = sprite;
        }
        
        public void LoadFromSprite(Sprite sprite)
        {
            if (sprite == null) return;

            Texture2D tex = sprite.texture;
            Rect rect = sprite.textureRect; 

            int texWidth = (int)rect.width;
            int texHeight = (int)rect.height;

            Color32[] pixels = tex.GetPixels32();

            for (int y = 0; y < m_height; y++)
            {
                for (int x = 0; x < m_width; x++)
                {
                    int px = (int)(rect.x + (float)x / m_width * texWidth);
                    int py = (int)(rect.y + (float)y / m_height * texHeight);

                    var color = pixels[py * tex.width + px];

                    int idx = Idx(x, y);

                    if (color.a > 10) 
                    {
                        _cells[idx] = CreateFilledCell(x, y, color);
                    }
                    else
                    {
                        _cells[idx] = CreateEmptyCell(x, y);
                    }
                }
            }

            _dirty = true;
        }

        public static List<ColorGroup> ExtractColorGroups(Sprite sprite, float distanceThreshold = 50f, byte alphaThreshold = 10)
        {
            var groups = new List<ColorGroup>();
            if (sprite == null) return groups;

            Texture2D texture = sprite.texture;
            Rect rect = sprite.textureRect;
            Color32[] pixels = texture.GetPixels32();

            int startX = Mathf.FloorToInt(rect.x);
            int startY = Mathf.FloorToInt(rect.y);
            int endX = startX + Mathf.FloorToInt(rect.width);
            int endY = startY + Mathf.FloorToInt(rect.height);

            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    var color = pixels[y * texture.width + x];
                    if (color.a <= alphaThreshold) continue;

                    ColorGroup bestGroup = null;
                    float bestDistance = float.MaxValue;

                    for (int i = 0; i < groups.Count; i++)
                    {
                        float distance = ColorDistance(groups[i].RepresentativeColor, color);
                        if (distance > distanceThreshold || distance >= bestDistance) continue;

                        bestDistance = distance;
                        bestGroup = groups[i];
                    }

                    if (bestGroup == null)
                    {
                        groups.Add(new ColorGroup(color));
                        continue;
                    }

                    bestGroup.AddSample(color);
                }
            }

            return groups;
        }

        public void RebuildTexture()
        {
            if (!_dirty) return;

            int texW = Texture.width;

            for (int y = 0; y < m_height; y++)
            {
                for (int x = 0; x < m_width; x++)
                {
                    var cell = _cells[Idx(x, y)];

                    Color32 color = cell.hasValue == 1 ? cell.color : m_backgroundColor;

                    int startX = x * _pixelsPerCell;
                    int startY = y * _pixelsPerCell;

                    for (int py = 0; py < _pixelsPerCell; py++)
                    {
                        int row = (startY + py) * texW;

                        for (int px = 0; px < _pixelsPerCell; px++)
                        {
                            _pixels[row + startX + px] = color;
                        }
                    }
                }
            }
            Texture.SetPixelData(_pixels, 0);
            Texture.Apply(false);
            _dirty = false;
        }
        
        private bool OutOfBound(int x, int y) => x < 0 || y < 0 || x >= m_width || y >= m_height;

        public bool InBound(int x, int y) => x >= 0 && y >= 0 && x < m_width && y < m_height;

        public void SetPixelCell(int x, int y, Color32 color32)
        {
            if (OutOfBound(x, y)) return;
            int idx = Idx(x, y);
            var c = _cells[idx];
            c.color = color32;
            c.hasValue = 1;
            c.x = x;
            c.y = y;
            _cells[idx] = c;
            _dirty = true;
        }

        public void ClearPixelCell(int x, int y, Color32 color32)
        {
            if (OutOfBound(x, y)) return;
            int idx = Idx(x, y);
            var cell = _cells[idx];
            cell.color = color32;
            cell.hasValue = 0;
            cell.x = x;
            cell.y = y;
            _cells[idx] = cell;
            _dirty = true;
        }
        
        public Cell GetCell(int x, int y)
        {
            if (OutOfBound(x, y))
            {
                return CreateFilledCell(x, y, Color.clear);
            }

            return _cells[Idx(x, y)];
        }

        public Color32 GetCellColor(int x, int y) => GetCell(x, y).color;

        public bool HasValue(int x, int y) => GetCell(x, y).hasValue == 1;

        public void SetPixelCellBatch(int x, int y, Color32 color32)
        {
            if (OutOfBound(x, y)) return;
            int idx = Idx(x, y);
            var c = _cells[idx];
            c.color = color32;
            c.hasValue = 1;
            c.x = x;
            c.y = y;
            _cells[idx] = c;
        }

        public void MarkDirty() => _dirty = true;

        public bool IsEmpty(int x, int y)
        {
            if (OutOfBound(x, y)) return false;
            var c = _cells[Idx(x, y)];
            return c.hasValue == 0;
        }

        public int ClearConnectedCellsByColor(int startX, int startY, Color32 targetColor, float distanceThreshold = 50f)
        {
            if (OutOfBound(startX, startY)) return 0;

            var startCell = _cells[Idx(startX, startY)];
            if (startCell.hasValue == 0) return 0;
            if (ColorDistance(startCell.color, targetColor) > distanceThreshold) return 0;

            var visited = new NativeArray<byte>(m_width * m_height, Allocator.Temp);
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(startX, startY));

            int cleared = 0;

            while (queue.Count > 0)
            {
                var pos = queue.Dequeue();
                if (OutOfBound(pos.x, pos.y)) continue;

                int idx = Idx(pos.x, pos.y);
                if (visited[idx] == 1) continue;
                visited[idx] = 1;

                var cell = _cells[idx];
                if (cell.hasValue == 0) continue;
                if (ColorDistance(cell.color, targetColor) > distanceThreshold) continue;

                _cells[idx] = CreateEmptyCell(pos.x, pos.y);
                cleared++;

                queue.Enqueue(new Vector2Int(pos.x + 1, pos.y));
                queue.Enqueue(new Vector2Int(pos.x - 1, pos.y));
                queue.Enqueue(new Vector2Int(pos.x, pos.y + 1));
                queue.Enqueue(new Vector2Int(pos.x, pos.y - 1));
            }

            visited.Dispose();

            if (cleared > 0)
            {
                _dirty = true;
            }

            return cleared;
        }

        private void ClearCells()
        {
            for (int x = 0; x < m_width; x++)
            for (int y = 0; y < m_height; y++)
            {
                _cells[Idx(x, y)] = CreateEmptyCell(x, y);
            }
        }

        private static Cell CreateEmptyCell(int x, int y)
        {
            return new Cell
            {
                x = x,
                y = y,
                hasValue = 0,
                color = Color.clear
            };
        }

        private static Cell CreateFilledCell(int x, int y, Color32 color)
        {
            return new Cell
            {
                x = x,
                y = y,
                hasValue = 1,
                color = color
            };
        }

        private static float ColorDistance(Color32 a, Color32 b)
        {
            int dr = a.r - b.r;
            int dg = a.g - b.g;
            int db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }
    }

    public struct Cell
    {
        public int x, y;
        public byte hasValue;
        public Color32 color;
    }

    public sealed class ColorGroup
    {
        public Color32 RepresentativeColor { get; private set; }
        public int Count { get; private set; }

        private int _sumR;
        private int _sumG;
        private int _sumB;
        private int _sumA;

        public ColorGroup(Color32 initialColor)
        {
            RepresentativeColor = initialColor;
            Count = 1;
            _sumR = initialColor.r;
            _sumG = initialColor.g;
            _sumB = initialColor.b;
            _sumA = initialColor.a;
        }

        public void AddSample(Color32 color)
        {
            Count++;
            _sumR += color.r;
            _sumG += color.g;
            _sumB += color.b;
            _sumA += color.a;

            RepresentativeColor = new Color32(
                (byte)(_sumR / Count),
                (byte)(_sumG / Count),
                (byte)(_sumB / Count),
                (byte)(_sumA / Count)
            );
        }
    }
}
