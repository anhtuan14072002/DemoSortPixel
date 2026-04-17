using UnityEngine;

namespace Pixel
{
    public struct ColorGroup
    {
        public Color Color;
        public int Count;

        public ColorGroup(Color color, int count)
        {
            Color = color;
            Count = count;
        }
    }
}